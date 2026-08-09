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
/// Registers services declared by convention on a module implementing <c>IConventionModule</c>.
/// </summary>
/// <remarks>
/// Ships as its own analyzer assembly rather than as another generator inside
/// DependencyModules.SourceGenerator, so a project that does not use conventions never loads the
/// class-scanning providers. It reuses that package's module discovery and emission by compiling in
/// the shared Impl sources; Impl declares no <c>[Generator]</c> of its own, so nothing is registered
/// twice when both packages are referenced.
/// </remarks>
[Generator]
public class ConventionSourceGenerator : BaseSourceGenerator {

    protected override IEnumerable<IDependencyModuleSourceGenerator> AttributeSourceGenerators() {
        yield return new ConventionGenerator();
    }

    // SetupRootGenerator is deliberately not overridden. DependencyModules.SourceGenerator owns the
    // module partial; emitting it from here too would declare every module twice.
}

/// <summary>
/// The convention half: discovery, matching and emission.
/// </summary>
public class ConventionGenerator : IDependencyModuleSourceGenerator {

    private const string LoggerName = "ConventionSourceGenerator";

    public void SetupGenerator(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<(ModuleEntryPointModel Left, DependencyModuleConfigurationModel Right)> incrementalValueProvider) {

        context.RegisterPostInitializationOutput(postInitContext =>
            postInitContext.AddSource(
                ConventionContractSource.HintName,
                SourceText.From(ConventionContractSource.Source, Encoding.UTF8)));

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

        var candidates = context.SyntaxProvider
            .CreateSyntaxProvider(
                ConventionCandidateUtility.IsCandidate,
                (syntaxContext, cancellation) =>
                    ConventionCandidateUtility.GetCandidateModel(syntaxContext, cancellation))
            .Where(model => !model.IsIgnored)
            .Collect();

        context.RegisterSourceOutput(
            incrementalValueProvider.Collect().Combine(conventionModules).Combine(candidates),
            GenerateSourceOutput);
    }

    private void GenerateSourceOutput(
        SourceProductionContext context,
        ((ImmutableArray<(ModuleEntryPointModel Left, DependencyModuleConfigurationModel Right)> Left,
            ImmutableArray<ConventionModuleModel> Right) Left,
            ImmutableArray<ConventionCandidateModel> Right) data) {

        var entryPoints = data.Left.Left;
        var conventionModules = data.Left.Right;
        var candidates = data.Right;

        if (entryPoints.Length == 0 || conventionModules.Length == 0) {
            return;
        }

        var configuration = entryPoints.First().Right;

        FileLogger.Wrap(
            LoggerName,
            configuration,
            logger => Generate(context, entryPoints, conventionModules, candidates, logger),
            // Surfaced as a build error rather than discarded, matching the attribute generators. A
            // generator that fails quietly produces a green build with no registrations.
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
        ImmutableArray<ConventionCandidateModel> candidates,
        FileLogger logger) {

        var (entryPointList, configurationModel) = EntryModelUtil.ConsolidateEntryPointModels(entryPoints);

        logger.Info(
            $"Discovered {conventionModules.Length} convention module(s) and " +
            $"{candidates.Length} candidate type(s).");

        var claimed = new HashSet<ConventionModuleModel>();

        foreach (var entryPointModel in entryPointList) {
            context.CancellationToken.ThrowIfCancellationRequested();

            var conventionModule = conventionModules.FirstOrDefault(
                module => module.ModuleType.Equals(entryPointModel.EntryPointType));

            if (conventionModule == null) {
                continue;
            }

            claimed.Add(conventionModule);

            GenerateForModule(
                context, entryPointModel, configurationModel, conventionModule, candidates, logger);
        }

        ReportUnclaimedModules(context, conventionModules, claimed, logger);
    }

    private void GenerateForModule(
        SourceProductionContext context,
        ModuleEntryPointModel entryPointModel,
        DependencyModuleConfigurationModel configurationModel,
        ConventionModuleModel conventionModule,
        ImmutableArray<ConventionCandidateModel> candidates,
        FileLogger logger) {

        var withNamespace = EntryModelUtil.EnsureNamespace(entryPointModel, configurationModel);

        var serviceModels = ConventionMatcher.Match(
            withNamespace,
            conventionModule,
            candidates,
            context.ReportDiagnostic,
            logger);

        if (serviceModels.Count == 0) {
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
    /// Reports a type that implements <c>IConventionModule</c> but is not a module.
    /// </summary>
    /// <remarks>
    /// Its conventions would otherwise produce nothing at all, with a green build and no
    /// explanation — exactly the silent failure the rest of this generator is built to avoid.
    /// </remarks>
    private static void ReportUnclaimedModules(
        SourceProductionContext context,
        ImmutableArray<ConventionModuleModel> conventionModules,
        HashSet<ConventionModuleModel> claimed,
        FileLogger logger) {

        foreach (var conventionModule in conventionModules) {
            if (claimed.Contains(conventionModule)) {
                continue;
            }

            var name = conventionModule.ModuleType.Name;

            logger.Error($"'{name}' implements IConventionModule but is not a [DependencyModule].");

            context.ReportDiagnostic(Diagnostic.Create(
                DependencyModuleDiagnostics.ConventionCannotBeRead,
                Location.None,
                "the declaring type is not marked with [DependencyModule], so it registers nothing",
                name));
        }
    }
}
