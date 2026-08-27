using CSharpAuthor;
using System.Collections.Immutable;
using System.Text;
using DependencyModules.Conventions.Models;
using DependencyModules.Conventions.Utilities;
using DependencyModules.SourceGenerator.Impl;
using DependencyModules.SourceGenerator.Impl.Models;
using DependencyModules.SourceGenerator.Impl.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DependencyModules.Conventions;

/// <summary>
/// A module-level <c>[Decorate]</c> after its decorator's constructor has been looked up.
/// </summary>
public record ResolvedModuleDecorator(
    ITypeDefinition ModuleType, DecoratorModel Model, string? Reason);

/// <summary>
/// The module-level decorations, and the compilation they were resolved from.
/// </summary>
/// <remarks>
/// The compilation travels with them rather than being combined into the output stage directly.
/// Combined directly it would change on every keystroke and re-run emission every time; carried
/// inside a value that compares on its resolved decorations alone, an edit that changes no
/// declaration propagates nothing.
///
/// It is needed at emission because a generic decorator may constrain its type parameters more
/// tightly than the service does, and whether a registration's arguments satisfy that is a question
/// only symbols can answer.
/// </remarks>
public record ModuleDecorators(
    EquatableList<ResolvedModuleDecorator> Resolved, Compilation Compilation) {

    public virtual bool Equals(ModuleDecorators? other) =>
        other is not null && Resolved.Equals(other.Resolved);

    public override int GetHashCode() => Resolved.GetHashCode();
}

/// <summary>
/// Convention registration: discovery, matching and emission.
/// </summary>
/// <remarks>
/// This shipped as its own analyzer package so a project that did not use conventions never loaded
/// the class-scanning provider. That boundary is gone, for two reasons that outweighed it.
///
/// A generic decorator has to be closed over the type arguments each registration used, and the
/// registrations conventions produce were invisible to the generator that emits decorations — so one
/// open-generic runtime call stood in for all of them, and that call cannot work in a published
/// Native AOT application. One assembly is what lets both paths emit closed calls.
///
/// And the scan is no longer what it was: the candidate transform is cached on the declaration and a
/// stamp of everything that can change what a name binds to, which took the per-keystroke cost from
/// 11–39 ms at 2,000 classes to a flat ~11 ms, and the convention half of that to ~2.4 ms.
/// </remarks>
public class ConventionGenerator : IDependencyModuleSourceGenerator {

    private const string LoggerName = "ConventionSourceGenerator";

    public void SetupGenerator(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<(ModuleEntryPointModel Left, DependencyModuleConfigurationModel Right)> incrementalValueProvider) {

        // The contracts used to be emitted here through RegisterPostInitializationOutput. They now
        // live in DependencyModules.Runtime, which is what lets them be public — and public is what
        // retires the explicit implementation requirement and the CS0436 between two assemblies that
        // both emitted them. Nothing is emitted before the pipeline any more.

        // Interface implementation rather than an attribute, so this cannot be
        // ForAttributeWithMetadataName. The predicate rejects on node type and base list before
        // looking at anything, which is the cheap kind of scan — the same reason module discovery
        // is still a syntax provider.
        // Lambdas rather than method groups: SyntaxTransformContext converts implicitly from
        // GeneratorSyntaxContext, but a method group conversion will not apply a user-defined
        // conversion to a parameter.
        var conventionModules = context.SyntaxProvider
            .CreateSyntaxProvider(
                ConventionModelUtility.IsConventionModuleCandidate,
                (syntaxContext, cancellation) =>
                    ConventionModelUtility.GetConventionModuleModel(syntaxContext, cancellation))
            .Where(model => !model.IsIgnored)
            .Collect();

        // Through the cache rather than straight to the utility. Roslyn re-runs this transform for
        // every candidate whenever any tree changes, so on a normal keystroke almost all of these
        // calls are recomputing a model identical to the one they produced last time.
        // Decorators, so a generic one can be expanded against what the conventions register. The
        // attribute path expands the same declaration against its own registrations; the two sets
        // are different, and DecoratorHelper refuses to apply one decorator to a descriptor twice.
        var decorators = AttributeModelCollector.Collect(
            context,
            new[] { KnownTypes.DependencyModules.Attributes.DecoratorAttribute },
            static (syntaxContext, cancellation) =>
                DecoratorModelUtility.GetDecoratorModel(syntaxContext, cancellation) ?? DecoratorModel.Ignore,
            new DecoratorModelComparer(),
            DecoratorModel.Ignore);

        // The registrations the *attributes* made. Decoration needs them and the convention ones in
        // the same place: a generic decorator is closed over the type arguments a registration used,
        // and expanding it twice against two halves of the picture is how the same declaration ended
        // up emitted from two stages that could not see each other.
        var attributeServices = AttributeModelCollector.Collect(
            context,
            new[] {
                KnownTypes.DependencyModules.Attributes.TransientServiceAttribute,
                KnownTypes.DependencyModules.Attributes.ScopedServiceAttribute,
                KnownTypes.DependencyModules.Attributes.SingletonServiceAttribute,
                KnownTypes.DependencyModules.Attributes.CrossWireServiceAttribute
            },
            static (syntaxContext, cancellation) =>
                ServiceModelUtility.GetServiceModel(syntaxContext, cancellation) ?? ServiceModel.Ignore,
            new ServiceModelComparer(),
            ServiceModel.Ignore);

        // [Decorate] on a module names its decorator by typeof(), so the constructor has to be
        // looked up from the compilation. Resolved here rather than in the output stage: the
        // compilation changes on every keystroke, and combining it into the output would re-emit
        // everything every time. The result is compared by value, so an unchanged lookup propagates
        // nothing — the same shape the metadata scan below uses, and for the same reason.
        var moduleDecorators = incrementalValueProvider.Collect()
            .Combine(context.CompilationProvider)
            .Select((pair, cancellation) => {
                var resolved = new List<ResolvedModuleDecorator>();

                foreach (var (entryPoint, _) in pair.Left) {
                    foreach (var resolution in
                             ModuleDecoratorResolver.Resolve(entryPoint, pair.Right, cancellation)) {
                        resolved.Add(new ResolvedModuleDecorator(
                            entryPoint.EntryPointType, resolution.Model, resolution.Reason));
                    }
                }

                return new ModuleDecorators(
                    new EquatableList<ResolvedModuleDecorator>(resolved), pair.Right);
            });

        var candidates = context.SyntaxProvider
            .CreateSyntaxProvider(
                ConventionCandidateUtility.IsCandidate,
                (syntaxContext, cancellation) =>
                    ConventionCandidateCache.GetOrAdd(syntaxContext, cancellation))
            .Where(model => !model.IsIgnored)
            .Collect();

        // Candidates from assemblies a convention names with InAssemblyOf<T>. Combined with the
        // compilation, so this Select re-runs whenever the compilation changes — which is every
        // keystroke — but its result is compared by value, so the emission downstream stays cached
        // unless the scanned assembly's public surface actually differs. When no convention names an
        // assembly it returns an empty list after one pass over the conventions, which is the common
        // case and costs nothing.
        var metadataCandidates = conventionModules
            .Combine(context.CompilationProvider)
            .Select((pair, cancellation) =>
                new EquatableList<ConventionCandidateModel>(
                    MetadataCandidateUtility.Collect(pair.Left, pair.Right, cancellation)));

        var everything = incrementalValueProvider.Collect()
            .Combine(conventionModules)
            .Combine(candidates)
            .Combine(metadataCandidates)
            .Combine(decorators)
            .Combine(attributeServices)
            .Combine(moduleDecorators);

        context.RegisterSourceOutput(everything, GenerateSourceOutput);

        // Diagnostics separately, with the compilation combined in, because a location needs the
        // syntax tree it came from before .editorconfig or #pragma can silence it and only the
        // compilation can find that tree.
        //
        // The compilation ModuleDecorators already carries cannot serve this. It rides inside a
        // value compared on its resolved decorations alone, precisely so an edit that changes no
        // declaration propagates nothing - which means that when nothing changed, the compilation
        // held there is the one from a previous run. Good enough for asking whether a decorator's
        // constraints admit a type; not good enough to hand Roslyn a tree that is no longer in the
        // compilation it is filtering against.
        //
        // The cost is that matching runs twice, once per output. The emitting pass is silent, so
        // nothing composes a diagnostic message it is about to discard, and this pass writes no
        // source.
        context.RegisterSourceOutput(everything.Combine(context.CompilationProvider), ReportDiagnostics);
    }

    private void GenerateSourceOutput(
        SourceProductionContext context,
        ((((((ImmutableArray<(ModuleEntryPointModel Left, DependencyModuleConfigurationModel Right)> Left,
            ImmutableArray<ConventionModuleModel> Right) Left,
            ImmutableArray<ConventionCandidateModel> Right) Left,
            EquatableList<ConventionCandidateModel> Right) Left,
            ImmutableArray<DecoratorModel> Right) Left,
            ImmutableArray<ServiceModel> Right) Left,
            ModuleDecorators Right) data) {

        var entryPoints = data.Left.Left.Left.Left.Left.Left;
        var conventionModules = data.Left.Left.Left.Left.Left.Right;
        var decorators = data.Left.Left.Right;
        var attributeServices = data.Left.Right;
        var moduleDecorators = data.Right;

        // In-compilation candidates and metadata candidates travel together; a convention sees one
        // source or the other, decided by whether it named an assembly.
        var candidates = data.Left.Left.Left.Left.Right.Length == 0
            ? (IReadOnlyList<ConventionCandidateModel>)data.Left.Left.Left.Right
            : data.Left.Left.Left.Left.Right.Concat(data.Left.Left.Left.Right).ToArray();

        // Decoration runs whether or not anything declares a convention, so the early-out is on
        // entry points alone.
        if (entryPoints.Length == 0) {
            return;
        }

        var configuration = entryPoints.First().Right;

        FileLogger.Wrap(
            LoggerName,
            configuration,
            logger => Generate(
                context, entryPoints, conventionModules, candidates, decorators, attributeServices,
                moduleDecorators, DiagnosticReporter.Silent, emit: true, logger),
            // Surfaced as a build error rather than discarded, matching the attribute generators. A
            // generator that fails quietly produces a green build with no registrations.
            exception => context.ReportDiagnostic(
                Diagnostic.Create(
                    DependencyModuleDiagnostics.GeneratorFailure,
                    Location.None,
                    $"{exception.GetType().Name}: {exception.Message}")));
    }

    /// <summary>
    /// Everything the convention pass has to say, reported against real syntax trees. Emits nothing.
    /// </summary>
    private void ReportDiagnostics(
        SourceProductionContext context,
        (((((((ImmutableArray<(ModuleEntryPointModel Left, DependencyModuleConfigurationModel Right)> Left,
            ImmutableArray<ConventionModuleModel> Right) Left,
            ImmutableArray<ConventionCandidateModel> Right) Left,
            EquatableList<ConventionCandidateModel> Right) Left,
            ImmutableArray<DecoratorModel> Right) Left,
            ImmutableArray<ServiceModel> Right) Left,
            ModuleDecorators Right) Left,
            Compilation Right) data) {

        var models = data.Left;
        var entryPoints = models.Left.Left.Left.Left.Left.Left;

        if (entryPoints.Length == 0) {
            return;
        }

        var candidates = models.Left.Left.Left.Left.Right.Length == 0
            ? (IReadOnlyList<ConventionCandidateModel>)models.Left.Left.Left.Right
            : models.Left.Left.Left.Left.Right.Concat(models.Left.Left.Left.Right).ToArray();

        var configuration = entryPoints.First().Right;
        var report = new DiagnosticReporter(context.ReportDiagnostic, new SyntaxTreeLookup(data.Right));

        FileLogger.Wrap(
            LoggerName,
            configuration,
            logger => Generate(
                context, entryPoints, models.Left.Left.Left.Left.Left.Right, candidates,
                models.Left.Left.Right, models.Left.Right, models.Right,
                report, emit: false, logger),
            exception => context.ReportDiagnostic(
                Diagnostic.Create(
                    DependencyModuleDiagnostics.GeneratorFailure,
                    Location.None,
                    $"{exception.GetType().Name}: {exception.Message}")));
    }

    private void Generate(
        SourceProductionContext context,
        ImmutableArray<(ModuleEntryPointModel Left, DependencyModuleConfigurationModel Right)> entryPoints,
        ImmutableArray<ConventionModuleModel> conventionModules,
        IReadOnlyList<ConventionCandidateModel> candidates,
        ImmutableArray<DecoratorModel> decorators,
        ImmutableArray<ServiceModel> attributeServices,
        ModuleDecorators moduleDecorators,
        DiagnosticReporter report,
        bool emit,
        FileLogger logger) {

        var (entryPointList, configurationModel) = EntryModelUtil.ConsolidateEntryPointModels(entryPoints);

        logger.Info(
            $"Discovered {conventionModules.Length} convention module(s) and " +
            $"{candidates.Count} candidate type(s).");

        var claimed = new HashSet<ConventionModuleModel>();

        // An auto-generated module deferring to a declared one is not among these, so its decorations
        // and convention registrations are emitted once rather than alongside an identical copy on
        // the module it defers to. It can never be a convention module itself - IConventionModule is
        // implemented by hand - so nothing here goes unclaimed as a result.
        foreach (var entryPointModel in EntryModelUtil.RegistrationTargets(entryPointList)) {
            context.CancellationToken.ThrowIfCancellationRequested();

            var conventionModule = conventionModules.FirstOrDefault(
                module => module.ModuleType.Equals(entryPointModel.EntryPointType));

            if (conventionModule != null) {
                claimed.Add(conventionModule);
            }

            GenerateForModule(
                context, entryPointModel, configurationModel, conventionModule, candidates, decorators,
                attributeServices, moduleDecorators, report, emit, logger);
        }

        ReportUnclaimedModules(report, conventionModules, claimed, logger);
    }

    private void GenerateForModule(
        SourceProductionContext context,
        ModuleEntryPointModel entryPointModel,
        DependencyModuleConfigurationModel configurationModel,
        ConventionModuleModel? conventionModule,
        IReadOnlyList<ConventionCandidateModel> candidates,
        ImmutableArray<DecoratorModel> decorators,
        ImmutableArray<ServiceModel> attributeServices,
        ModuleDecorators moduleDecorators,
        DiagnosticReporter report,
        bool emit,
        FileLogger logger) {

        var withNamespace = EntryModelUtil.EnsureNamespace(entryPointModel, configurationModel);

        var serviceModels = conventionModule == null
            ? Array.Empty<ServiceModel>()
            : ConventionMatcher.Match(
                withNamespace, conventionModule, candidates, report, logger);

        // Every registration this compilation makes, however it was declared. This is the whole
        // point of the single stage: a generic decorator is expanded once, against all of them.
        WriteDecorators(
            context, withNamespace, configurationModel,
            ServiceTypes(attributeServices, serviceModels), decorators, moduleDecorators,
            report, emit, logger);

        if (serviceModels.Count == 0 || !emit) {
            return;
        }

        // coverageAttributeOnMethod: the registrations file already puts ExcludeFromCodeCoverage on
        // the partial class, and the attribute is not AllowMultiple, so a second class-level one on
        // the same type is CS0579.
        var writer = new DependencyFileWriter(logger, coverageAttributeOnMethod: true);

        var output = writer.Write(withNamespace, configurationModel, serviceModels, "Convention");

        context.AddSource(
            withNamespace.EntryPointType.GetFileNameHint(
                configurationModel.RootNamespace, "ConventionDependencies"),
            output);
    }

    /// <summary>
    /// Every service type the compilation registers, in the closed form it registers it as.
    /// </summary>
    private static IReadOnlyList<ITypeDefinition> ServiceTypes(
        ImmutableArray<ServiceModel> attributeServices, IReadOnlyList<ServiceModel> conventionServices) {

        var seen = new HashSet<ITypeDefinition>();
        var ordered = new List<ITypeDefinition>();

        void Add(IEnumerable<ServiceModel> models) {
            foreach (var model in models) {
                if (model.Equals(ServiceModel.Ignore)) {
                    continue;
                }

                foreach (var registration in model.Registrations) {
                    if (seen.Add(registration.ServiceType)) {
                        ordered.Add(registration.ServiceType);
                    }
                }
            }
        }

        Add(attributeServices);
        Add(conventionServices);

        return ordered;
    }

    /// <summary>
    /// Emits every decoration for one module, from one place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to be two stages — one expanding a generic decorator against the attribute
    /// registrations, one against the convention registrations — and neither could see the other's
    /// set. The same declaration was emitted twice, each half believing the other had nothing, and
    /// the only thing standing between that and a service wrapped twice was a run-time guard.
    /// </para>
    /// <para>
    /// One stage with every registration in hand is what makes the expansion answerable: a generic
    /// decorator is closed once, over each construction the compilation actually registers.
    /// </para>
    /// </remarks>
    private static void WriteDecorators(
        SourceProductionContext context,
        ModuleEntryPointModel entryPointModel,
        DependencyModuleConfigurationModel configurationModel,
        IReadOnlyList<ITypeDefinition> registeredServiceTypes,
        ImmutableArray<DecoratorModel> declared,
        ModuleDecorators moduleDecorators,
        DiagnosticReporter report,
        bool emit,
        FileLogger logger) {

        var decorators = CollectDecorators(report, entryPointModel, declared, moduleDecorators, logger);

        if (decorators.Count == 0) {
            return;
        }

        var expanded = DecoratorExpansion.Expand(
            decorators,
            registeredServiceTypes,
            out var refusedForOpenGenericRegistration,
            canClose: (decoratorType, closedService) =>
                DecoratorConstraintChecker.CanClose(
                    moduleDecorators.Compilation, decoratorType, closedService));

        ReportOpenGenericDecoration(report, refusedForOpenGenericRegistration, logger);

        ReportImplementationUnderFactories(
            report, decorators, entryPointModel, configurationModel, logger);

        if (expanded.Count == 0 || !emit) {
            return;
        }

        logger.Info($"{expanded.Count} decoration(s) for {entryPointModel.EntryPointType.Name}.");

        var output = new DecoratorFileWriter().Write(entryPointModel, configurationModel, expanded);

        context.AddSource(
            entryPointModel.EntryPointType.GetFileNameHint(
                configurationModel.RootNamespace, "Decorators"),
            output);
    }

    /// <summary>
    /// The decorators that belong to one module: those declared on a class, filtered by realm, plus
    /// those the module declares itself with <c>[Decorate]</c>.
    /// </summary>
    private static IReadOnlyList<DecoratorModel> CollectDecorators(
        DiagnosticReporter report,
        ModuleEntryPointModel entryPointModel,
        ImmutableArray<DecoratorModel> declared,
        ModuleDecorators moduleDecorators,
        FileLogger logger) {

        var decorators = new List<DecoratorModel>();

        foreach (var decorator in declared) {
            if (decorator.IsIgnored) {
                continue;
            }

            // A realm-scoped decorator belongs only to its realm. An unscoped one belongs to every
            // module that is not realm-only, matching how service registrations behave.
            if (decorator.Realm != null) {
                if (decorator.Realm.Equals(entryPointModel.EntryPointType)) {
                    decorators.Add(decorator);
                }

                continue;
            }

            if (!entryPointModel.ModuleFeatures.HasFlag(ModuleEntryPointFeatures.OnlyRealm)) {
                decorators.Add(decorator);
            }
        }

        // [Decorate] carries two type names and nothing else, so its decorator's constructor is
        // looked up rather than read from a declaration — the only route for one declared in a
        // referenced assembly, which is the case the module-level form exists for.
        foreach (var resolution in moduleDecorators.Resolved) {
            if (!resolution.ModuleType.Equals(entryPointModel.EntryPointType)) {
                continue;
            }

            if (resolution.Reason != null) {
                logger.Error(
                    $"'{resolution.Model.DecoratorType.Name}' cannot be constructed by generated " +
                    $"code: {resolution.Reason}.");
            }

            decorators.Add(resolution.Model);
        }

        ReportAmbiguousOrdering(report, decorators, logger);

        return decorators;
    }

    /// <summary>
    /// Reports a decorator naming an implementation in a project that emits factories.
    /// </summary>
    /// <remarks>
    /// The decorator would wrap every registration of the service instead of the one it named,
    /// because a factory descriptor cannot say what it built. An intercepted service is exempted
    /// from the property automatically - the interception is declared on the class being registered,
    /// so the writer emitting that registration sees it. A decorator is declared on the decorator,
    /// and the registration it targets is written by a pass that never learns about it.
    /// </remarks>
    private static void ReportImplementationUnderFactories(
        DiagnosticReporter report,
        IReadOnlyList<DecoratorModel> decorators,
        ModuleEntryPointModel entryPointModel,
        DependencyModuleConfigurationModel configurationModel,
        FileLogger logger) {

        if (!entryPointModel.GenerateFactories.GetValueOrDefault(configurationModel.GenerateFactories)) {
            return;
        }

        foreach (var decorator in decorators) {
            if (decorator.Implementation == null) {
                continue;
            }

            logger.Error(
                $"'{decorator.DecoratorType.Name}' names an implementation, which generated " +
                "factories cannot be told apart by.");

            report.Report(
                DependencyModuleDiagnostics.DecoratorImplementationNeedsTypeRegistration,
                decorator.Location,
                decorator.DecoratorType.Name,
                decorator.Implementation.Name,
                decorator.ServiceType.Name);
        }
    }

    /// <summary>
    /// Reports the decorators that were dropped because their service is registered as an open
    /// generic.
    /// </summary>
    /// <remarks>
    /// The expansion cannot produce anything for these, and until this existed the two shapes failed
    /// differently and both badly: a generic decorator vanished with a green build, and a non-generic
    /// one reached emission carrying an unbound service type and produced CS7003 in generated code.
    /// </remarks>
    private static void ReportOpenGenericDecoration(
        DiagnosticReporter report,
        IReadOnlyList<DecoratorModel> refused,
        FileLogger logger) {

        foreach (var decorator in refused) {
            var serviceName = decorator.ServiceType.Name;
            var decoratorName = decorator.DecoratorType.Name;

            logger.Error(
                $"'{decoratorName}' cannot decorate '{serviceName}' because it is registered as an " +
                "open generic.");

            report.Report(
                DependencyModuleDiagnostics.OpenGenericCannotBeDecorated,
                decorator.Location,
                serviceName,
                decoratorName);
        }
    }

    /// <summary>
    /// Two decorators of one service sharing an order nest in an order nobody declared, so it is
    /// reported rather than resolved arbitrarily.
    /// </summary>
    private static void ReportAmbiguousOrdering(
        DiagnosticReporter report, IReadOnlyList<DecoratorModel> decorators, FileLogger logger) {

        for (var i = 0; i < decorators.Count; i++) {
            for (var j = i + 1; j < decorators.Count; j++) {
                if (decorators[i].Order != decorators[j].Order ||
                    !decorators[i].ServiceType.Equals(decorators[j].ServiceType)) {
                    continue;
                }

                logger.Error(
                    $"'{decorators[i].DecoratorType.Name}' and '{decorators[j].DecoratorType.Name}' both " +
                    $"decorate '{decorators[i].ServiceType.Name}' with order {decorators[i].Order}.");

                report.Report(
                    DependencyModuleDiagnostics.AmbiguousDecoratorOrder,
                    decorators[i].Location,
                    decorators[i].DecoratorType.Name,
                    decorators[j].DecoratorType.Name,
                    decorators[i].ServiceType.Name,
                    decorators[i].Order);
            }
        }
    }

    /// <summary>
    /// Reports a type that implements <c>IConventionModule</c> but is not a module.
    /// </summary>
    /// <remarks>
    /// Its conventions would otherwise produce nothing at all, with a green build and no
    /// explanation — exactly the silent failure the rest of this generator is built to avoid.
    /// </remarks>
    private static void ReportUnclaimedModules(
        DiagnosticReporter report,
        ImmutableArray<ConventionModuleModel> conventionModules,
        HashSet<ConventionModuleModel> claimed,
        FileLogger logger) {

        foreach (var conventionModule in conventionModules) {
            if (claimed.Contains(conventionModule)) {
                continue;
            }

            var name = conventionModule.ModuleType.Name;

            logger.Error($"'{name}' implements IConventionModule but is not a [DependencyModule].");

            report.Report(
                DependencyModuleDiagnostics.ConventionCannotBeRead,
                conventionModule.Location,
                "the declaring type is not marked with [DependencyModule], so it registers nothing",
                name);
        }
    }
}
