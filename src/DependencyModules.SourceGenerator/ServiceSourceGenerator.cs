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
            return;
        }
        
        var (entryPointList, configurationModel) = 
            EntryModelUtil.ConsolidateEntryPointModels(inputData.Left);
        
        foreach (var entryPointModel in entryPointList) {
            context.CancellationToken.ThrowIfCancellationRequested();
            GenerateSourceOutput(context, entryPointModel, configurationModel, inputData.Right, logger);
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

    protected override IEqualityComparer<ServiceModel> GetComparer() {
        return _serviceEqualityComparer;
    }

    protected override ServiceModel GenerateAttributeModel(GeneratorSyntaxContext context, CancellationToken cancellationToken) {
        var serviceModel = ServiceModelUtility.GetServiceModel(context, cancellationToken);
        
        return serviceModel ?? ServiceModel.Ignore;
    }
}