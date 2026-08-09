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
/// <param name="Interface">
/// Null when the convention named no service type. A filter-selected convention matches the type
/// itself rather than through anything it implements.
/// </param>
public record ConventionRegistrationMatch(
    ConventionModel Convention,
    ConventionCandidateModel Candidate,
    ImplementedInterfaceModel? Interface);

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

        var merged = MergePartialDeclarations(candidates);

        var matches = new List<ConventionRegistrationMatch>();

        foreach (var convention in conventionModule.Conventions) {
            CollectMatches(convention, merged, moduleName, matches, report, logger);
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

        var serviceName = convention.DisplayName;

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

            if (!convention.NamespaceMatches(candidate.ImplementationType.Namespace)) {
                continue;
            }

            // With no service type the filters are the whole selection, and the candidate matches
            // as itself — one match, no interface.
            List<ImplementedInterfaceModel?> matched;

            if (convention.ServiceType == null) {
                matched = new List<ImplementedInterfaceModel?> { null };
            }
            else {
                var reachable = AllMatchingInterfaces(convention, candidate);

                if (reachable.Count == 0) {
                    continue;
                }

                // AsSelf and AsSelfWithInterfaces name the implementation, so several matching
                // closings still produce one registration rather than one per closing.
                matched = convention.RegisterAs == ConventionRegisterAs.Interfaces
                    ? reachable.Cast<ImplementedInterfaceModel?>().ToList()
                    : new List<ImplementedInterfaceModel?> { reachable[0] };
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

            foreach (var candidateInterface in matched) {
                matches.Add(new ConventionRegistrationMatch(convention, candidate, candidateInterface));
            }
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
    /// Every construction of the convention's service type this candidate implements.
    /// </summary>
    /// <remarks>
    /// All of them, not the first. A notification handler covering two events implements
    /// <c>INotificationHandler&lt;OrderPlaced&gt;</c> and <c>INotificationHandler&lt;OrderShipped&gt;</c>,
    /// and registering only the first left the second silently unregistered — a green build and an
    /// event that never fires. They are different service types, so registering both is not the same
    /// implementation appearing twice.
    /// </remarks>
    private static List<ImplementedInterfaceModel> AllMatchingInterfaces(
        ConventionModel convention, ConventionCandidateModel candidate) {

        var matched = new List<ImplementedInterfaceModel>();

        foreach (var candidateInterface in candidate.InterfacesInReach(convention.IncludeBaseClasses)) {
            // An open convention matches any closing, and the candidate is registered against the
            // construction it actually implements. A closed one has to match exactly, or
            // RegisterAll<IHandler<A,B>>() would pick up every other closing as well.
            var isMatch = convention.IsOpenGeneric
                ? convention.DefinitionKey == candidateInterface.DefinitionKey
                : convention.ServiceType!.Equals(candidateInterface.InterfaceType);

            if (isMatch) {
                matched.Add(candidateInterface);
            }
        }

        return matched;
    }

    /// <summary>
    /// Collapses the declarations of one partial type into a single candidate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The candidate provider runs per declaration, and <c>CollectInterfaces</c> reads the base list
    /// of the declaration in front of it rather than the interfaces of the merged symbol. So
    /// <c>partial class Foo : IFoo</c> in one part and <c>partial class Foo : FooBase</c> in another
    /// produce two candidates that each see half of the picture, and the ambiguity check — which
    /// groups by implementation type — read the second one as a competing match and refused both.
    /// </para>
    /// <para>
    /// Merging rather than deduping the matches, because the halves are genuinely different: the
    /// interface a convention matches through decides whether the match is by declaration or through
    /// a base class, and which of the two parts happened to be visited first should not decide that.
    /// A type that declares the interface in any part declares it.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<ConventionCandidateModel> MergePartialDeclarations(
        IReadOnlyList<ConventionCandidateModel> candidates) {

        var byType = new Dictionary<ITypeDefinition, List<ConventionCandidateModel>>();
        var order = new List<ITypeDefinition>();
        var anyPartial = false;

        foreach (var candidate in candidates) {
            if (!byType.TryGetValue(candidate.ImplementationType, out var parts)) {
                parts = new List<ConventionCandidateModel>();
                byType[candidate.ImplementationType] = parts;
                order.Add(candidate.ImplementationType);
            }
            else {
                anyPartial = true;
            }

            parts.Add(candidate);
        }

        if (!anyPartial) {
            return candidates;
        }

        var merged = new List<ConventionCandidateModel>(order.Count);

        foreach (var implementationType in order) {
            var parts = byType[implementationType];

            merged.Add(parts.Count == 1 ? parts[0] : MergeParts(parts));
        }

        return merged;
    }

    private static ConventionCandidateModel MergeParts(List<ConventionCandidateModel> parts) {
        var declared = new List<ImplementedInterfaceModel>();
        var viaBaseClass = new List<ImplementedInterfaceModel>();
        var seen = new HashSet<ITypeDefinition>();

        // Declared first across every part, so an interface written on one declaration is a declared
        // match even when another part only reaches it through a base class. That is what makes
        // IncludeBaseClasses() unnecessary for a type that names the interface anywhere.
        foreach (var part in parts) {
            foreach (var declaredInterface in part.DeclaredInterfaces) {
                if (seen.Add(declaredInterface.InterfaceType)) {
                    declared.Add(declaredInterface);
                }
            }
        }

        foreach (var part in parts) {
            foreach (var baseClassInterface in part.BaseClassInterfaces) {
                if (seen.Add(baseClassInterface.InterfaceType)) {
                    viaBaseClass.Add(baseClassInterface);
                }
            }
        }

        var conditions = new List<EnvironmentConditionModel>();

        // Attributes on partial parts combine, so the conditions do too.
        foreach (var part in parts) {
            if (part.Conditions != null) {
                conditions.AddRange(part.Conditions);
            }
        }

        return parts[0] with {
            DeclaredInterfaces = declared,
            BaseClassInterfaces = viaBaseClass,
            // The greediest constructor, matching what GetConstructorInfo does within one
            // declaration. A part that declares none reports an empty parameter list, so taking the
            // first part's would emit a call to a constructor the type does not have.
            Constructor = GreediestConstructor(parts),
            Conditions = conditions.Count > 0 ? conditions : null,
        };
    }

    private static ConstructorInfoModel? GreediestConstructor(List<ConventionCandidateModel> parts) {
        ConstructorInfoModel? greediest = null;

        foreach (var part in parts) {
            if (part.Constructor == null) {
                continue;
            }

            if (greediest == null || part.Constructor.Parameters.Count > greediest.Parameters.Count) {
                greediest = part.Constructor;
            }
        }

        return greediest;
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

        // Keyed on what the match will actually register as, not on the implementation alone. A type
        // filling two roles registers twice; one service type claimed twice is the ambiguity.
        var byRegistration =
            new Dictionary<(ITypeDefinition Implementation, ITypeDefinition Service),
                List<ConventionRegistrationMatch>>();

        var order = new List<(ITypeDefinition Implementation, ITypeDefinition Service)>();

        foreach (var match in matches) {
            var key = RegistrationKey(match);

            if (!byRegistration.TryGetValue(key, out var list)) {
                list = new List<ConventionRegistrationMatch>();
                byRegistration[key] = list;
                order.Add(key);
            }

            list.Add(match);
        }

        var usable = new List<ConventionRegistrationMatch>();

        // Insertion order rather than dictionary order: the emitted registration order feeds the
        // module snapshots, and a hash order would move them for unrelated reasons.
        foreach (var key in order) {
            var group = byRegistration[key];

            if (group.Count == 1) {
                usable.Add(group[0]);

                continue;
            }

            var first = group[0];
            var second = group[1];
            var serviceName = ServiceTypeNameOf(first);

            var difference = first.Convention.Lifestyle == second.Convention.Lifestyle
                ? "The declaration is duplicated."
                : $"They declare different lifetimes ({first.Convention.Lifestyle} and " +
                  $"{second.Convention.Lifestyle}).";

            logger.Error(
                $"{moduleName}: '{first.Candidate.ImplementationType.Name}' is registered as " +
                $"'{serviceName}' by two conventions. {difference}");

            report(Diagnostic.Create(
                DependencyModuleDiagnostics.AmbiguousConventionMatch,
                first.Candidate.Location.ToLocationOrNone(),
                first.Candidate.ImplementationType.Name,
                moduleName,
                serviceName,
                difference));
        }

        return usable;
    }

    /// <summary>
    /// Identifies the registration a match produces, so two matches collide only when they would
    /// put the same implementation in the container under the same service type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keyed on the type definitions rather than on <see cref="ConventionTypeKey"/>, which is
    /// deliberately equal for every closing of one generic so an open convention can match them all.
    /// Using it here made <c>IHandler&lt;A&gt;</c> and <c>IHandler&lt;B&gt;</c> look like one service
    /// type and reported a handler covering two messages as a duplicate declaration.
    /// </para>
    /// <para>
    /// The shapes that name the implementation itself — <c>AsSelf</c> and
    /// <c>AsSelfWithInterfaces</c> — key on it, since two such conventions on one type would collide
    /// however their interface sets differ.
    /// </para>
    /// </remarks>
    private static (ITypeDefinition Implementation, ITypeDefinition Service) RegistrationKey(
        ConventionRegistrationMatch match) {

        var implementation = match.Candidate.ImplementationType;

        return match.Convention.RegisterAs == ConventionRegisterAs.Interfaces && match.Interface != null
            ? (implementation, match.Interface.InterfaceType)
            : (implementation, implementation);
    }

    private static string ServiceTypeNameOf(ConventionRegistrationMatch match) =>
        match.Convention.RegisterAs == ConventionRegisterAs.Interfaces && match.Interface != null
            ? match.Interface.InterfaceType.Name
            : match.Candidate.ImplementationType.Name;

    /// <summary>
    /// Reports DM0010 on each registered class, naming the interface it was reached through when the
    /// match was not direct.
    /// </summary>
    private static void ReportExposure(
        IReadOnlyList<ConventionRegistrationMatch> matches, string moduleName, Action<Diagnostic> report) {

        foreach (var match in matches) {
            // A filter-selected convention reached the type directly, so there is no interface to
            // name and the type is exposed as itself.
            if (match.Interface == null) {
                report(Diagnostic.Create(
                    DependencyModuleDiagnostics.ExposedByConvention,
                    match.Candidate.Location.ToLocationOrNone(),
                    $"{match.Candidate.ImplementationType.Name} in {moduleName}"));

                continue;
            }

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

        // One model per implementation, carrying every registration it produces — the shape
        // ServiceModelUtility builds for the attribute path. Emitting one model per match would put
        // two models with the same ImplementationType in front of DependencyFileWriter, which
        // duplicates the per-implementation state it reads: constructor, conditions, cross-wire.
        var byImplementation = new Dictionary<ITypeDefinition, List<ServiceRegistrationModel>>();
        var order = new List<ConventionRegistrationMatch>();

        foreach (var match in matches) {
            var registrations = BuildRegistrations(match, entryPointModel);

            if (registrations.Count == 0) {
                continue;
            }

            if (!byImplementation.TryGetValue(match.Candidate.ImplementationType, out var list)) {
                list = new List<ServiceRegistrationModel>();
                byImplementation[match.Candidate.ImplementationType] = list;
                order.Add(match);
            }

            list.AddRange(registrations);
        }

        var models = new List<ServiceModel>(order.Count);

        foreach (var match in order) {
            var registrations = byImplementation[match.Candidate.ImplementationType];

            logger.Info(
                $"  {match.Candidate.ImplementationType.Name} -> " +
                $"{string.Join(", ", registrations.Select(r => r.ServiceType.Name))} " +
                "(by convention)");

            models.Add(new ServiceModel(
                match.Candidate.ImplementationType,
                match.Candidate.Constructor,
                null,
                null,
                registrations,
                RegistrationFeature.None,
                // Shared across every match for this type, so the first one carries them all.
                match.Candidate.Conditions));
        }

        return models;
    }

    /// <summary>
    /// The registrations one match produces, according to what the convention registers matches as.
    /// </summary>
    /// <remarks>
    /// <c>AsSelfWithInterfaces</c> is the existing cross-wire emission rather than a second
    /// mechanism: one registration per interface carrying <see cref="ServiceRegistrationModel.CrossWire"/>,
    /// which the writer turns into a factory resolving the implementation type, plus a single
    /// registration of that type. Two plain registrations of the same implementation would give one
    /// instance per service type instead of the shared instance the contract promises.
    /// </remarks>
    private static IReadOnlyList<ServiceRegistrationModel> BuildRegistrations(
        ConventionRegistrationMatch match, ModuleEntryPointModel entryPointModel) {

        var convention = match.Convention;
        var lifestyle = convention.Lifestyle!.Value;

        // Scoped to the declaring module. Without this an OnlyRealm module would drop its own
        // convention registrations, because the writer skips a registration with no realm when the
        // module declares one.
        var realm = entryPointModel.EntryPointType;

        ServiceRegistrationModel Registration(ITypeDefinition serviceType, bool crossWire = false) =>
            new(serviceType,
                lifestyle,
                convention.RegistrationType,
                realm,
                convention.Key,
                crossWire,
                convention.KeyNamespaces);

        switch (convention.RegisterAs) {
            case ConventionRegisterAs.Self:
                return new[] { Registration(match.Candidate.ImplementationType) };

            case ConventionRegisterAs.SelfAndInterfaces:
                var crossWired = new List<ServiceRegistrationModel>();

                foreach (var reachable in
                         match.Candidate.InterfacesInReach(convention.IncludeBaseClasses)) {
                    crossWired.Add(Registration(reachable.InterfaceType, crossWire: true));
                }

                // No interfaces at all still registers the type itself, which is what the developer
                // asked for by naming a shape that includes "self".
                if (crossWired.Count == 0) {
                    crossWired.Add(Registration(match.Candidate.ImplementationType));
                }

                return crossWired;

            default:
                return new[] { Registration(match.Interface!.InterfaceType) };
        }
    }
}
