using CSharpAuthor;
using DependencyModules.Conventions.Models;
using DependencyModules.SourceGenerator.Impl;
using DependencyModules.SourceGenerator.Impl.Models;
using DependencyModules.SourceGenerator.Impl.Utilities;
using Microsoft.CodeAnalysis;

namespace DependencyModules.Conventions.Utilities;

/// <summary>
/// One candidate matched by one convention, through one interface.
/// </summary>
public record ConventionRegistrationMatch(
    ConventionModel Convention,
    ConventionCandidateModel Candidate,
    ImplementedInterfaceModel Interface);

/// <summary>
/// Matches a module's conventions against the candidates in the compilation.
/// </summary>
/// <remarks>
/// This runs at output time rather than in a transform, so it can report diagnostics and does not
/// have to be cacheable. Everything it works from is already rendered to strings and
/// <see cref="ITypeDefinition"/>s, so no symbol is touched here.
/// </remarks>
public static class ConventionMatcher {

    public static IReadOnlyList<ServiceModel> Match(
        ModuleEntryPointModel entryPointModel,
        ConventionModuleModel conventionModule,
        IReadOnlyList<ConventionCandidateModel> candidates,
        Action<Diagnostic> report,
        FileLogger logger) {

        var moduleName = entryPointModel.EntryPointType.Name;

        foreach (var unreadable in conventionModule.Unreadable) {
            logger.Error($"{moduleName}: refused '{unreadable.Text}' — {unreadable.Reason}.");

            report(Diagnostic.Create(
                DependencyModuleDiagnostics.ConventionCannotBeRead,
                unreadable.Location.ToLocationOrNone(),
                unreadable.Reason,
                unreadable.Text));
        }

        var matches = new List<ConventionRegistrationMatch>();

        foreach (var convention in conventionModule.Conventions) {
            CollectMatches(convention, candidates, moduleName, matches, report, logger);
        }

        var usable = RemoveAmbiguous(matches, moduleName, report, logger);

        ReportExposure(usable, moduleName, report);

        return BuildServiceModels(usable, entryPointModel, logger);
    }

    private static void CollectMatches(
        ConventionModel convention,
        IReadOnlyList<ConventionCandidateModel> candidates,
        string moduleName,
        List<ConventionRegistrationMatch> matches,
        Action<Diagnostic> report,
        FileLogger logger) {

        var serviceName = convention.ServiceType.Name;

        if (convention.Lifestyle == null) {
            logger.Error($"{moduleName}: the convention registering '{serviceName}' declared no lifetime.");

            report(Diagnostic.Create(
                DependencyModuleDiagnostics.ConventionCannotBeRead,
                convention.Location.ToLocationOrNone(),
                "no lifetime was declared; call AsSingleton(), AsScoped() or AsTransient()",
                $"RegisterAll({serviceName})"));

            return;
        }

        var found = 0;

        foreach (var candidate in candidates) {
            if (candidate.IsIgnored) {
                continue;
            }

            var matched = FirstMatchingInterface(convention, candidate);

            if (matched == null) {
                continue;
            }

            found++;

            // Reported rather than registered: a registration the container cannot construct throws
            // when the provider is built, a long way from the convention responsible.
            if (!candidate.HasAccessibleConstructor) {
                logger.Error(
                    $"{moduleName}: '{candidate.ImplementationType.Name}' matched '{serviceName}' " +
                    "but has no accessible constructor.");

                report(Diagnostic.Create(
                    DependencyModuleDiagnostics.ConventionMatchNotConstructable,
                    candidate.Location.ToLocationOrNone(),
                    candidate.ImplementationType.Name,
                    serviceName,
                    moduleName));

                continue;
            }

            matches.Add(new ConventionRegistrationMatch(convention, candidate, matched));
        }

        if (found == 0) {
            logger.Info($"{moduleName}: the convention registering '{serviceName}' matched nothing.");

            report(Diagnostic.Create(
                DependencyModuleDiagnostics.ConventionMatchedNothing,
                convention.Location.ToLocationOrNone(),
                serviceName,
                moduleName));
        }
    }

    /// <summary>
    /// The construction of the convention's service type that this candidate implements, if any.
    /// </summary>
    /// <remarks>
    /// One interface per candidate per convention. A candidate implementing two closings of the same
    /// open generic is registered against the first; registering it against both would mean the same
    /// implementation appearing twice for one declaration.
    /// </remarks>
    private static ImplementedInterfaceModel? FirstMatchingInterface(
        ConventionModel convention, ConventionCandidateModel candidate) {

        foreach (var candidateInterface in candidate.InterfacesInReach(convention.IncludeBaseClasses)) {
            // An open convention matches any closing, and the candidate is registered against the
            // construction it actually implements. A closed one has to match exactly, or
            // RegisterAll<IHandler<A,B>>() would pick up every other closing as well.
            var isMatch = convention.IsOpenGeneric
                ? convention.DefinitionKey == candidateInterface.DefinitionKey
                : convention.ServiceType.Equals(candidateInterface.InterfaceType);

            if (isMatch) {
                return candidateInterface;
            }
        }

        return null;
    }

    /// <summary>
    /// Drops any candidate two conventions in the same module both claim.
    /// </summary>
    /// <remarks>
    /// Reported rather than resolved. Picking one silently produces a registration nobody can
    /// predict from reading the module, which is the outcome DM0004 exists to prevent.
    /// </remarks>
    private static List<ConventionRegistrationMatch> RemoveAmbiguous(
        List<ConventionRegistrationMatch> matches,
        string moduleName,
        Action<Diagnostic> report,
        FileLogger logger) {

        var byCandidate = new Dictionary<ITypeDefinition, List<ConventionRegistrationMatch>>();

        foreach (var match in matches) {
            if (!byCandidate.TryGetValue(match.Candidate.ImplementationType, out var list)) {
                list = new List<ConventionRegistrationMatch>();
                byCandidate[match.Candidate.ImplementationType] = list;
            }

            list.Add(match);
        }

        var usable = new List<ConventionRegistrationMatch>();

        foreach (var pair in byCandidate) {
            if (pair.Value.Count == 1) {
                usable.Add(pair.Value[0]);

                continue;
            }

            var first = pair.Value[0];
            var second = pair.Value[1];

            logger.Error(
                $"{moduleName}: '{pair.Key.Name}' is matched by conventions for " +
                $"'{first.Convention.ServiceType.Name}' and '{second.Convention.ServiceType.Name}'.");

            report(Diagnostic.Create(
                DependencyModuleDiagnostics.AmbiguousConventionMatch,
                first.Candidate.Location.ToLocationOrNone(),
                pair.Key.Name,
                moduleName,
                first.Convention.ServiceType.Name,
                second.Convention.ServiceType.Name));
        }

        return usable;
    }

    /// <summary>
    /// Reports DM0010 on each registered class, naming the interface it was reached through when the
    /// match was not direct.
    /// </summary>
    private static void ReportExposure(
        IReadOnlyList<ConventionRegistrationMatch> matches, string moduleName, Action<Diagnostic> report) {

        foreach (var match in matches) {
            var via = match.Interface.ViaTypeName == null ? "" : $" (via {match.Interface.ViaTypeName})";

            report(Diagnostic.Create(
                DependencyModuleDiagnostics.ExposedByConvention,
                match.Candidate.Location.ToLocationOrNone(),
                $"{match.Interface.InterfaceType.Name} in {moduleName}{via}"));
        }
    }

    /// <summary>
    /// Produces the same models the attribute path produces, so emission needs no special case.
    /// </summary>
    private static IReadOnlyList<ServiceModel> BuildServiceModels(
        IReadOnlyList<ConventionRegistrationMatch> matches,
        ModuleEntryPointModel entryPointModel,
        FileLogger logger) {

        var models = new List<ServiceModel>(matches.Count);

        foreach (var match in matches) {
            logger.Info(
                $"  {match.Candidate.ImplementationType.Name} -> {match.Interface.InterfaceType.Name} " +
                $"({match.Convention.Lifestyle}, by convention)");

            models.Add(new ServiceModel(
                match.Candidate.ImplementationType,
                match.Candidate.Constructor,
                null,
                null,
                new[] {
                    new ServiceRegistrationModel(
                        match.Interface.InterfaceType,
                        match.Convention.Lifestyle!.Value,
                        // Scoped to the declaring module. Without this an OnlyRealm module would
                        // drop its own convention registrations, because the writer skips a
                        // registration with no realm when the module declares one.
                        Realm: entryPointModel.EntryPointType)
                },
                RegistrationFeature.None));
        }

        return models;
    }
}
