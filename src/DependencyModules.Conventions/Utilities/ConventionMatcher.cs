using CSharpAuthor;
using DependencyModules.Conventions.Models;
using DependencyModules.SourceGenerator.Impl;
using DependencyModules.SourceGenerator.Impl.Models;
using DependencyModules.SourceGenerator.Impl.Utilities;
using System.Text.RegularExpressions;
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

        // Compiled once per convention rather than once per candidate, which is the whole reason
        // the model keeps patterns as strings.
        var nameFilters = CompileNameFilters(convention);

        foreach (var candidate in candidates) {
            if (candidate.IsIgnored) {
                continue;
            }

            // One source or the other. A scan of the project being built must not pick up a type
            // from a package, and a scan of a package must not pick up a local one.
            if (candidate.AssemblyName != convention.AssemblyName) {
                continue;
            }

            if (!convention.NamespaceMatches(candidate.ImplementationType.Namespace) ||
                !convention.AttributesMatch(candidate.AttributeTypeKeys) ||
                !NameMatches(nameFilters, candidate.ImplementationType)) {
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
                    candidate.Location == LocationModel.None
                        ? convention.Location.ToLocationOrNone()
                        : candidate.Location.ToLocation(),
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
    /// Translates each name glob to an anchored regular expression, once per convention.
    /// </summary>
    /// <remarks>
    /// Two wildcards and nothing else: <c>*</c> for zero or more characters, <c>?</c> for exactly
    /// one. Everything else is escaped, so a pattern cannot smuggle in a regex.
    /// </remarks>
    private static List<(Regex Pattern, bool Qualified, bool Exclude)>? CompileNameFilters(
        ConventionModel convention) {

        if (convention.NameFilters == null) {
            return null;
        }

        var compiled = new List<(Regex, bool, bool)>(convention.NameFilters.Count);

        foreach (var filter in convention.NameFilters) {
            var expression = "^" +
                             Regex.Escape(filter.Pattern).Replace("\\*", ".*").Replace("\\?", ".") +
                             "$";

            // Ordinal and case-sensitive, consistent with C# identifiers.
            compiled.Add((new Regex(expression, RegexOptions.CultureInvariant), filter.IsQualified,
                filter.Exclude));
        }

        return compiled;
    }

    private static bool NameMatches(
        List<(Regex Pattern, bool Qualified, bool Exclude)>? filters, ITypeDefinition implementation) {

        if (filters == null) {
            return true;
        }

        var bareName = implementation.Name;

        var qualifiedName = string.IsNullOrEmpty(implementation.Namespace)
            ? bareName
            : implementation.Namespace + "." + bareName;

        var included = false;
        var anyInclusion = false;

        foreach (var (pattern, qualified, exclude) in filters) {
            var subject = qualified ? qualifiedName : bareName;

            if (exclude) {
                if (pattern.IsMatch(subject)) {
                    return false;
                }

                continue;
            }

            anyInclusion = true;
            included |= pattern.IsMatch(subject);
        }

        return !anyInclusion || included;
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

        var attributeKeys = new List<string>();

        // Attributes on partial parts combine, so the keys do too.
        foreach (var part in parts) {
            if (part.AttributeTypeKeys != null) {
                foreach (var key in part.AttributeTypeKeys) {
                    if (!attributeKeys.Contains(key)) {
                        attributeKeys.Add(key);
                    }
                }
            }
        }

        return parts[0] with {
            AttributeTypeKeys = attributeKeys.Count > 0 ? attributeKeys : null,
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
                LocationOf(first),
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

        return match.Convention.RegisterAs switch {
            ConventionRegisterAs.Explicit => (implementation, match.Convention.ExplicitServiceType!),
            ConventionRegisterAs.MatchingInterface =>
                (implementation, MatchingInterfaceOf(match) ?? implementation),
            ConventionRegisterAs.Interfaces when match.Interface != null =>
                (implementation, match.Interface.InterfaceType),
            _ => (implementation, implementation),
        };
    }

    /// <summary>
    /// Where to report a diagnostic about a match.
    /// </summary>
    /// <remarks>
    /// The class itself when it is in this compilation, which is the affordance that makes DM0010
    /// worth having — "this type is in the container as IFoo" answered in the IDE, at the type. A
    /// match read out of a referenced assembly has no class to squiggle, so it reports at the
    /// convention that asked for it, which is the only place the developer can act on it.
    /// </remarks>
    private static Location LocationOf(ConventionRegistrationMatch match) =>
        match.Candidate.Location == LocationModel.None
            ? match.Convention.Location.ToLocationOrNone()
            : match.Candidate.Location.ToLocation();

    /// <summary>
    /// Whether an interface is one the BCL declares, and so not something to expand a service into.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Applies only to the <c>AsSelfWithInterfaces</c> expansion, where the interfaces are whatever
    /// the type happens to reach rather than anything the developer named. Without it, any type whose
    /// base implements <see cref="IDisposable"/> becomes resolvable as <c>IDisposable</c>, and a
    /// FluentValidation validator becomes resolvable as <c>IEnumerable&lt;IValidationRule&gt;</c> —
    /// neither of which is what "register this as its interfaces" means.
    /// </para>
    /// <para>
    /// One rule rather than a list. Autofac excludes <c>IDisposable</c> and not <c>IEnumerable</c>;
    /// Scrutor excludes <c>IEnumerable</c> and not <c>IDisposable</c>. Both are patches added after
    /// users hit them, and the list they imply keeps growing — <c>IEquatable&lt;T&gt;</c>,
    /// <c>IComparable</c>, <c>ICloneable</c>. Excluding the <c>System</c> namespace covers the family
    /// at once and is defensible in a sentence.
    /// </para>
    /// <para>
    /// Deliberately not extended to <c>Microsoft.Extensions.*</c>: a <c>BackgroundService</c>
    /// reaching <c>IHostedService</c> is something a developer could legitimately want cross-wired,
    /// so that line does not hold the way this one does.
    /// </para>
    /// </remarks>
    private static bool IsFrameworkInterface(ITypeDefinition interfaceType) {
        var namespaceName = interfaceType.Namespace;

        return namespaceName == "System" ||
               (namespaceName != null && namespaceName.StartsWith("System.", StringComparison.Ordinal));
    }

    /// <summary>
    /// The interface named after the implementation — Foo as IFoo.
    /// </summary>
    private static ITypeDefinition? MatchingInterfaceOf(ConventionRegistrationMatch match) {
        var wanted = "I" + match.Candidate.ImplementationType.Name;

        foreach (var candidateInterface in
                 match.Candidate.InterfacesInReach(match.Convention.IncludeBaseClasses)) {
            if (string.Equals(candidateInterface.InterfaceType.Name, wanted, StringComparison.Ordinal)) {
                return candidateInterface.InterfaceType;
            }
        }

        return null;
    }

    private static string ServiceTypeNameOf(ConventionRegistrationMatch match) =>
        RegistrationKey(match).Service.Name;

    /// <summary>
    /// Reports DM0010 on each registered class, naming the interface it was reached through when the
    /// match was not direct.
    /// </summary>
    private static void ReportExposure(
        IReadOnlyList<ConventionRegistrationMatch> matches, string moduleName, Action<Diagnostic> report) {

        foreach (var match in matches) {
            var location = LocationOf(match);

            // A filter-selected convention reached the type directly, so there is no interface to
            // name and the type is exposed as itself.
            if (match.Interface == null) {
                report(Diagnostic.Create(
                    DependencyModuleDiagnostics.ExposedByConvention,
                    location,
                    $"{match.Candidate.ImplementationType.Name} in {moduleName}"));

                continue;
            }

            var via = match.Interface.ViaTypeName == null ? "" : $" (via {match.Interface.ViaTypeName})";

            report(Diagnostic.Create(
                DependencyModuleDiagnostics.ExposedByConvention,
                location,
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

            case ConventionRegisterAs.Explicit:
                return new[] { Registration(convention.ExplicitServiceType!) };

            case ConventionRegisterAs.MatchingInterface:
                var matching = MatchingInterfaceOf(match);

                // Skipped rather than registered some other way: the convention asked for one shape
                // and this type does not have it.
                return matching == null
                    ? Array.Empty<ServiceRegistrationModel>()
                    : new[] { Registration(matching) };

            case ConventionRegisterAs.SelfAndInterfaces:
                var crossWired = new List<ServiceRegistrationModel>();

                foreach (var reachable in
                         match.Candidate.InterfacesInReach(convention.IncludeBaseClasses)) {
                    if (IsFrameworkInterface(reachable.InterfaceType)) {
                        continue;
                    }

                    crossWired.Add(Registration(reachable.InterfaceType, crossWire: true));
                }

                // Nothing left to expand to still registers the type itself, so filtering everything
                // away degrades to AsSelf() rather than to nothing.
                if (crossWired.Count == 0) {
                    crossWired.Add(Registration(match.Candidate.ImplementationType));
                }

                return crossWired;

            default:
                return new[] { Registration(match.Interface!.InterfaceType) };
        }
    }
}
