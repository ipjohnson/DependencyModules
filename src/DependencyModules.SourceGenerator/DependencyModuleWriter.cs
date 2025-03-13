using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl;
using DependencyModules.SourceGenerator.Impl.Models;
using DependencyModules.SourceGenerator.Impl.Utilities;
using Microsoft.CodeAnalysis;
using static CSharpAuthor.SyntaxHelpers;

namespace DependencyModules.SourceGenerator;

public class DependencyModuleWriter {

    public void GenerateSource(SourceProductionContext context, 
        (ModuleEntryPointModel Left, DependencyModuleConfigurationModel Right) models) {
        var model = models.Left;

        var csharpFile = new CSharpFileDefinition(model.EntryPointType.Namespace);

        GenerateModuleClass(model, csharpFile);

        GenerateUseMethod(models, csharpFile);
        
        GenerateAttribute(models, csharpFile);
        
        var outputContext = new OutputContext();

        csharpFile.WriteOutput(outputContext);

        context.AddSource(model.EntryPointType.Name + "." + model.UniqueId() + ".Module.g.cs", outputContext.Output());
    }

    private void GenerateAttribute((ModuleEntryPointModel Left, DependencyModuleConfigurationModel Right) models, CSharpFileDefinition csharpFile) {
        var model = models.Left;
        if (model.GenerateAttribute != false) {
            var attributeGenerator = new ModuleAttributeWriter();

            attributeGenerator.CreateAttributeClass(csharpFile, model);
        }
    }

    private void GenerateUseMethod((ModuleEntryPointModel Left, DependencyModuleConfigurationModel Right) models, CSharpFileDefinition csharpFile) {

        if (string.IsNullOrEmpty(models.Left.UseMethod)) {
            return;
        }
        var extensionMethod = csharpFile.AddClass($"{models.Left.EntryPointType.Name}Extensions");

        extensionMethod.Modifiers = ComponentModifier.Public | ComponentModifier.Static | ComponentModifier.Partial;
        
        var method = extensionMethod.AddMethod(models.Left.UseMethod!);

        method.Modifiers = ComponentModifier.Public | ComponentModifier.Static;
        method.SetReturnType(KnownTypes.Microsoft.DependencyInjection.IServiceCollection);

        var serviceProvider = method.AddParameter(KnownTypes.Microsoft.DependencyInjection.IServiceCollection, "serviceCollection");
        serviceProvider.This = true;
        
        var parameters = new List<object>();
        
        foreach (var parameterInfoModel in models.Left.Parameters) {
            var param = method.AddParameter(parameterInfoModel.ParameterType, parameterInfoModel.ParameterName);
            
            parameters.Add(param);
        }
        
        var newStatement = New(models.Left.EntryPointType, parameters.ToArray());
        
        method.Return(serviceProvider.Invoke("AddModules", newStatement));
        method.AddUsingNamespace("DependencyModules.Runtime");
    }
    
    private void GenerateModuleClass(ModuleEntryPointModel model, CSharpFileDefinition csharpFile) {
        var classDefinition = csharpFile.AddClass(model.EntryPointType.Name);

        classDefinition.EnableNullable();
        classDefinition.Modifiers |= ComponentModifier.Partial;
        
        SetupStaticConstructor(classDefinition);

        PopulateServiceCollectionMethod(classDefinition, model);

        InternalApplyServicesMethod(classDefinition, model);

        InternalGetModulesMethod(classDefinition, model);
        
        FeatureMethod(classDefinition, model);

        if (!model.ImplementsEquals) {
            EqualMethod(classDefinition, model);

            HashMethod(classDefinition);
        }
    }

    private void FeatureMethod(ClassDefinition classDefinition, ModuleEntryPointModel model) {
        if (model.Features.Count == 0) {
            return;
        }

        classDefinition.AddBaseType(KnownTypes.DependencyModules.Features.IDependencyModuleApplicatorProvider);

        var method = classDefinition.AddMethod("FeatureApplicators");
        method.SetReturnType(
            new GenericTypeDefinition(typeof(IEnumerable<>), new []{KnownTypes.DependencyModules.Features.IFeatureApplicator}));

        method.Modifiers |= ComponentModifier.Virtual | ComponentModifier.Public;

        method.AddLeadingTrait(CodeOutputComponent.Get("[Browsable(false)]", true));
        method.AddUsingNamespace("System.ComponentModel");
        
        method.InterfaceImplementation = KnownTypes.DependencyModules.Features.IDependencyModuleApplicatorProvider;
        
        foreach (var typeDefinition in model.Features) {
            method.AddIndentedStatement(
                YieldReturn(
                    New(
                        new GenericTypeDefinition(TypeDefinitionEnum.ClassDefinition,
                        KnownTypes.DependencyModules.Features.FeatureApplicator.Namespace,
                        KnownTypes.DependencyModules.Features.FeatureApplicator.Name,
                        new []{typeDefinition}), "this")
                    ));
        }
    }

    private void HashMethod(
        ClassDefinition classDefinition) {
        var hashMethod = classDefinition.AddMethod("GetHashCode");

        hashMethod.Modifiers |= ComponentModifier.Override;
        hashMethod.SetReturnType(typeof(int));

        hashMethod.Return("HashCode.Combine(base.GetHashCode())");
    }

    private void EqualMethod(ClassDefinition classDefinition, ModuleEntryPointModel model) {
        var equalMethod = classDefinition.AddMethod("Equals");

        equalMethod.Modifiers |= ComponentModifier.Override;
        equalMethod.SetReturnType(typeof(bool));

        equalMethod.AddParameter(TypeDefinition.Get(typeof(object)).MakeNullable(), "obj");

        equalMethod.Return($"obj is {model.EntryPointType.Name}");
    }

    private void InternalGetModulesMethod(ClassDefinition classDefinition, ModuleEntryPointModel model) {
        var attributeModels = FilterAttributes(model.AttributeModels);
        
        if (attributeModels.Count == 0) {
            return;
        }
        
        var getModulesMethod = classDefinition.AddMethod("InternalGetModules");

        getModulesMethod.AddLeadingTrait(CodeOutputComponent.Get("[Browsable(false)]", true));
        getModulesMethod.AddUsingNamespace("System.ComponentModel");
        
        getModulesMethod.InterfaceImplementation = KnownTypes.DependencyModules.Interfaces.IDependencyModule;
        getModulesMethod.SetReturnType(TypeDefinition.Get(typeof(IEnumerable<object>)));

        foreach (var modelAttributeModel in attributeModels) {
            var newStatement = New(modelAttributeModel.TypeDefinition, modelAttributeModel.ArgumentString);

            var initValue = modelAttributeModel.PropertyString;
            
            if (!string.IsNullOrEmpty(initValue)) {
                newStatement.AddInitValue(initValue);
            }

            getModulesMethod.AddIndentedStatement(YieldReturn(newStatement));
        }
    }

    private List<AttributeModel> FilterAttributes(IReadOnlyList<AttributeModel> modelAttributeModels) {
        var attributeModels = new List<AttributeModel>();

        foreach (var modelAttributeModel in modelAttributeModels) {
            if (modelAttributeModel.TypeDefinition.Name == "DependencyModuleAttribute") {
                continue;
            }

            attributeModels.Add(modelAttributeModel);
        }
        
        return attributeModels;
    }

    private void InternalApplyServicesMethod(
        ClassDefinition classDefinition,
        ModuleEntryPointModel model) {

        var loadDependenciesMethod = classDefinition.AddMethod("InternalApplyServices");

        loadDependenciesMethod.AddLeadingTrait(CodeOutputComponent.Get("[Browsable(false)]", true));
        loadDependenciesMethod.AddUsingNamespace("System.ComponentModel");
        
        loadDependenciesMethod.InterfaceImplementation =
            KnownTypes.DependencyModules.Interfaces.IDependencyModule;

        var parameter =
            loadDependenciesMethod.AddParameter(
                KnownTypes.Microsoft.DependencyInjection.IServiceCollection, "services");

        var closedType = new GenericTypeDefinition(
            TypeDefinitionEnum.ClassDefinition, KnownTypes.DependencyModules.Helpers.Namespace, "DependencyRegistry", new[] {
                model.EntryPointType
            });

        loadDependenciesMethod.AddIndentedStatement(
            new StaticInvokeStatement(
                closedType,
                "ApplyServices",
                new IOutputComponent[] {
                    parameter
                }) {
                Indented = false
            });
    }

    private void PopulateServiceCollectionMethod(ClassDefinition classDefinition, ModuleEntryPointModel model) {
        classDefinition.AddBaseType(KnownTypes.DependencyModules.Interfaces.IDependencyModule);

        var loadDependenciesMethod = classDefinition.AddMethod("PopulateServiceCollection");

        var parameter =
            loadDependenciesMethod.AddParameter(
                KnownTypes.Microsoft.DependencyInjection.IServiceCollection, "services");

        var closedType = new GenericTypeDefinition(
            TypeDefinitionEnum.ClassDefinition,
            KnownTypes.DependencyModules.Helpers.Namespace,
            "DependencyRegistry",
            new[] {
                model.EntryPointType
            });

        loadDependenciesMethod.AddIndentedStatement(
            new StaticInvokeStatement(
                closedType,
                "LoadModules",
                new IOutputComponent[] {
                    parameter,
                    new CodeOutputComponent("this") {
                        Indented = false
                    }
                }) {
                Indented = false
            });
    }

    private void SetupStaticConstructor(ClassDefinition classDefinition) {
        classDefinition.AddConstructor().Modifiers = ComponentModifier.Static;
    }

}