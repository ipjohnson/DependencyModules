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

        var outputContext = new OutputContext();

        csharpFile.WriteOutput(outputContext);

        context.AddSource(model.EntryPointType.Name + ".Module.g.cs", outputContext.Output());
    }

    private void GenerateModuleClass(ModuleEntryPointModel model, CSharpFileDefinition csharpFile) {
        var classDefinition = csharpFile.AddClass(model.EntryPointType.Name);

        classDefinition.EnableNullable();
        classDefinition.Modifiers |= ComponentModifier.Partial;

        if (model.GenerateAttribute != false) {
            var attributeGenerator = new ModuleAttributeGenerator();

            attributeGenerator.Generate(model, classDefinition);
        }
        
        SetupStaticConstructor(classDefinition);

        PopulateServiceCollectionMethod(classDefinition, model);

        InternalApplyServicesMethod(classDefinition, model);

        InternalGetModulesMethod(classDefinition, model);

        if (!model.ImplementsEquals) {
            EqualMethod(classDefinition, model);

            HashMethod(classDefinition);
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
        
        var loadDependenciesMethod = classDefinition.AddMethod("InternalGetModules");

        loadDependenciesMethod.InterfaceImplementation = KnownTypes.DependencyModules.Interfaces.IDependencyModule;
        loadDependenciesMethod.SetReturnType(TypeDefinition.Get(typeof(IEnumerable<object>)));

        foreach (var modelAttributeModel in attributeModels) {
            var newStatement = New(modelAttributeModel.TypeDefinition, modelAttributeModel.ArgumentString);

            var initValue = modelAttributeModel.PropertyString;
            
            if (!string.IsNullOrEmpty(initValue)) {
                newStatement.AddInitValue(initValue);
            }

            loadDependenciesMethod.AddIndentedStatement(YieldReturn(newStatement));
        }
    }

    private List<AttributeModel> FilterAttributes(List<AttributeModel> modelAttributeModels) {
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