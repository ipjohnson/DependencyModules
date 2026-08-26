using System.Collections.Immutable;
using System.ComponentModel;
using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl;
using DependencyModules.SourceGenerator.Impl.Models;
using DependencyModules.SourceGenerator.Impl.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyModules.SourceGenerator;

public class ServiceSourceGenerator : BaseAttributeSourceGenerator<ServiceModel> {
    private static ITypeDefinition[] _skipTypes = new [] { TypeDefinition.Get(typeof(INotifyPropertyChanged))};
    private static readonly ITypeDefinition[] _attributeTypes = {
        KnownTypes.DependencyModules.Attributes.TransientServiceAttribute, 
        KnownTypes.DependencyModules.Attributes.ScopedServiceAttribute, 
        KnownTypes.DependencyModules.Attributes.SingletonServiceAttribute,
        KnownTypes.DependencyModules.Attributes.CrossWireServiceAttribute,
        KnownTypes.Microsoft.TextJson.JsonSourceGenerationOptionsAttribute
    };

    private readonly IEqualityComparer<ServiceModel> _serviceEqualityComparer = new ServiceModelComparer();

    protected override IEnumerable<ITypeDefinition> AttributeTypes() {
        return _attributeTypes;
    }

    protected override void GenerateSourceOutput(SourceProductionContext context, 
        (ImmutableArray<(ModuleEntryPointModel Left, DependencyModuleConfigurationModel Right)> Left, ImmutableArray<ServiceModel> Right) inputData, 
        FileLogger logger) {
        if (inputData.Left.Length == 0 || inputData.Right.Length == 0) {
            logger.Info(
                $"Nothing to generate: {inputData.Left.Length} module(s) and " +
                $"{inputData.Right.Length} service(s) were discovered.");
            return;
        }

        LogDiscovery(inputData, logger);

        var serviceModels = ReportUnconstructableServices(context, inputData.Right, logger);

        serviceModels = ReportCrossWiredGenerics(context, serviceModels, logger);

        if (serviceModels.Length == 0) {
            return;
        }

        ReportEnvironmentConditions(context, serviceModels, logger);

        var (entryPointList, configurationModel) =
            EntryModelUtil.ConsolidateEntryPointModels(inputData.Left);

        foreach (var entryPointModel in EntryModelUtil.RegistrationTargets(entryPointList)) {
            context.CancellationToken.ThrowIfCancellationRequested();
            GenerateSourceOutput(context, entryPointModel, configurationModel, serviceModels, logger);
        }
    }

    protected void GenerateSourceOutput(SourceProductionContext context,
        ModuleEntryPointModel entryPointModel,
        DependencyModuleConfigurationModel configurationModel,
        ImmutableArray<ServiceModel> serviceModels, FileLogger logger) {

        // don't generate empty dependency registrations
        if (serviceModels.Length == 0) {
            return;
        }
        
        entryPointModel = EntryModelUtil.EnsureNamespace(entryPointModel,configurationModel);
        
        var writer = new DependencyFileWriter(logger);

        var output = writer.Write(entryPointModel, configurationModel, serviceModels, "Module");
        
        context.AddSource(
            entryPointModel.EntryPointType.GetFileNameHint(
                configurationModel.RootNamespace,"Dependencies"), output);
    }

    /// <summary>
    /// Records what the generator saw and how it was configured.
    /// </summary>
    /// <remarks>
    /// This is what someone reporting a problem attaches. "My service is not registered" is
    /// answered by whether the service was discovered at all, which module realm it landed in, and
    /// what configuration was in effect — none of which is visible from the generated output alone.
    /// </remarks>
    private static void LogDiscovery(
        (ImmutableArray<(ModuleEntryPointModel Left, DependencyModuleConfigurationModel Right)> Left,
            ImmutableArray<ServiceModel> Right) inputData,
        FileLogger logger) {

        var configuration = inputData.Left.First().Right;

        logger.Info(
            "Configuration: " +
            $"RootNamespace='{configuration.RootNamespace}', " +
            $"ProjectDir='{configuration.ProjectDir}', " +
            $"RegistrationType={configuration.RegistrationType}, " +
            $"AutoGenerateModule={configuration.AutoGenerateEntry}, " +
            $"GenerateFactories={configuration.GenerateFactories}, " +
            $"ExcludeGeneratedCodeFromCoverage={configuration.ExcludeGeneratedCodeFromCoverage}");

        logger.Info($"Discovered {inputData.Left.Length} module(s):");
        foreach (var (entryPoint, _) in inputData.Left) {
            logger.Info(
                $"  {entryPoint.EntryPointType.Namespace}.{entryPoint.EntryPointType.Name} " +
                $"[{entryPoint.ModuleFeatures}] from '{entryPoint.FileLocation}'");
        }

        logger.Info($"Discovered {inputData.Right.Length} service(s):");
        foreach (var serviceModel in inputData.Right) {
            foreach (var registration in serviceModel.Registrations) {
                logger.Info(
                    $"  {serviceModel.ImplementationType.Name} -> {registration.ServiceType.Name} " +
                    $"({registration.Lifestyle}" +
                    (registration.Key != null ? $", key={registration.Key}" : "") +
                    (registration.Realm != null ? $", realm={registration.Realm.Name}" : "") +
                    (registration.RegistrationType != null ? $", using={registration.RegistrationType}" : "") +
                    ")");
            }
        }
    }

    /// <summary>
    /// Reports the services the container could never construct and removes them from generation.
    /// Emitting a registration for an abstract or static type produces code that throws when the
    /// provider is built, a long way from the declaration responsible.
    /// </summary>
    private static ImmutableArray<ServiceModel> ReportUnconstructableServices(
        SourceProductionContext context, ImmutableArray<ServiceModel> serviceModels, FileLogger logger) {

        if (!serviceModels.Any(IsUnconstructable)) {
            return serviceModels;
        }

        var builder = ImmutableArray.CreateBuilder<ServiceModel>(serviceModels.Length);

        foreach (var serviceModel in serviceModels) {
            if (!IsUnconstructable(serviceModel)) {
                builder.Add(serviceModel);
                continue;
            }

            var reason = serviceModel.Features.HasFlag(RegistrationFeature.StaticImplementation)
                ? "a static class"
                : "abstract";

            var typeName = serviceModel.ImplementationType.Name;

            logger.Error($"Skipping '{typeName}' because it is {reason}.");

            context.ReportDiagnostic(
                Diagnostic.Create(
                    DependencyModuleDiagnostics.ServiceCannotBeConstructed,
                    serviceModel.Location?.ToLocationOrNone() ?? Location.None,
                    typeName,
                    reason));
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Reports cross-wired generic types and removes them from generation.
    /// </summary>
    /// <remarks>
    /// Cross-wiring shares one instance across every service type, which is emitted as a factory per
    /// interface. An open generic registration cannot carry a factory, so the emission was invalid on
    /// its face: the type parameter leaked into the registration as <c>typeof(ILedger&lt;T&gt;)</c>
    /// and the factory read <c>GetRequiredService&lt;Ledger&lt;&gt;&gt;()</c>, giving CS0246 and
    /// CS7003 in generated code.
    ///
    /// The whole model is dropped rather than the cross-wired registrations alone. Keeping the
    /// implementation's own registration would honour half an attribute — the instance would no
    /// longer be reachable through any of its interfaces, which is the entire reason the attribute
    /// was written.
    /// </remarks>
    private static ImmutableArray<ServiceModel> ReportCrossWiredGenerics(
        SourceProductionContext context, ImmutableArray<ServiceModel> serviceModels, FileLogger logger) {

        if (!serviceModels.Any(IsCrossWiredGeneric)) {
            return serviceModels;
        }

        var builder = ImmutableArray.CreateBuilder<ServiceModel>(serviceModels.Length);

        foreach (var serviceModel in serviceModels) {
            if (!IsCrossWiredGeneric(serviceModel)) {
                builder.Add(serviceModel);

                continue;
            }

            var typeName = serviceModel.ImplementationType.Name;

            logger.Error($"Skipping '{typeName}' because a generic type cannot be cross-wired.");

            context.ReportDiagnostic(
                Diagnostic.Create(
                    DependencyModuleDiagnostics.CrossWireCannotBeGeneric,
                    serviceModel.Location?.ToLocationOrNone() ?? Location.None,
                    typeName));
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// A cross-wired registration on an implementation that is itself generic.
    /// </summary>
    private static bool IsCrossWiredGeneric(ServiceModel serviceModel) =>
        serviceModel.ImplementationType is GenericTypeDefinition { TypeArguments.Count: > 0 } &&
        serviceModel.Registrations.Any(registration => registration.CrossWire == true);

    /// <summary>
    /// Reports what each conditional registration depends on, and refuses conditions that name
    /// nothing to test.
    /// </summary>
    /// <remarks>
    /// Whether a condition holds is a run-time question, so there is no build error to raise for the
    /// ordinary case and DM0011 is informational. DM0012 is the case that is decidable here: a
    /// condition with no name or no key does not depend on the environment at all, whatever it was
    /// meant to say.
    /// </remarks>
    private static void ReportEnvironmentConditions(
        SourceProductionContext context, ImmutableArray<ServiceModel> serviceModels, FileLogger logger) {

        foreach (var serviceModel in serviceModels) {
            if (serviceModel.Conditions is not { Count: > 0 } conditions) {
                continue;
            }

            var typeName = serviceModel.ImplementationType.Name;

            foreach (var condition in conditions) {
                if (!EnvironmentConditionUtility.IsEmpty(condition)) {
                    continue;
                }

                var kind = condition.Kind == EnvironmentConditionKind.Name ? "environment name" : "key";

                logger.Error($"'{typeName}' has an environment condition that names no {kind}.");

                context.ReportDiagnostic(
                    Diagnostic.Create(
                        DependencyModuleDiagnostics.EmptyEnvironmentCondition,
                        serviceModel.Location?.ToLocationOrNone() ?? Location.None,
                        typeName,
                        kind));
            }

            var described = conditions
                .Where(condition => !EnvironmentConditionUtility.IsEmpty(condition))
                .Select(EnvironmentConditionUtility.Describe)
                .ToArray();

            if (described.Length == 0) {
                continue;
            }

            var summary = string.Join(" and ", described);

            logger.Info($"'{typeName}' registers only when {summary}.");

            context.ReportDiagnostic(
                Diagnostic.Create(
                    DependencyModuleDiagnostics.RegisteredConditionally,
                    serviceModel.Location?.ToLocationOrNone() ?? Location.None,
                    summary));
        }
    }

    private static bool IsUnconstructable(ServiceModel serviceModel) =>
        // A factory supplies the instance, so the declaring type never has to be constructed.
        serviceModel.Factory == null &&
        (serviceModel.Features.HasFlag(RegistrationFeature.AbstractImplementation) ||
         serviceModel.Features.HasFlag(RegistrationFeature.StaticImplementation));

    protected override ServiceModel IgnoredModel => ServiceModel.Ignore;

    protected override IEqualityComparer<ServiceModel> GetComparer() {
        return _serviceEqualityComparer;
    }

    protected override ServiceModel GenerateAttributeModel(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken) {
        var serviceModel = ServiceModelUtility.GetServiceModel(context, cancellationToken);
        
        return serviceModel ?? ServiceModel.Ignore;
    }
}