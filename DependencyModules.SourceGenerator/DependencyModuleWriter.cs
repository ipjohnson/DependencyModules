using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl;
using DependencyModules.SourceGenerator.Impl.Models;
using Microsoft.CodeAnalysis;
using static CSharpAuthor.SyntaxHelpers;

namespace DependencyModules.SourceGenerator;

public class DependencyModuleWriter {

    public void GenerateSource(SourceProductionContext context, ModuleEntryPointModel model) {

        var csharpFile = new CSharpFileDefinition(model.EntryPointType.Namespace);

        csharpFile.AddComponent(CodeOutputComponent.Get("#nullable enable"));
        GenerateModuleClass(model, csharpFile);

        var outputContext = new OutputContext();

        csharpFile.WriteOutput(outputContext);

        context.AddSource(model.EntryPointType.Name + ".Module.g.cs", outputContext.Output());
    }

    private void GenerateModuleClass(ModuleEntryPointModel model, CSharpFileDefinition csharpFile) {
        var classDefinition = csharpFile.AddClass(model.EntryPointType.Name);

        classDefinition.Modifiers |= ComponentModifier.Partial;

        var attributeGenerator = new ModuleAttributeGenerator();

        attributeGenerator.Generate(model, classDefinition);

        SetupStaticConstructor(classDefinition);

        PopulateServiceCollectionMethod(classDefinition, model);

        ApplyServicesMethod(classDefinition, model);

        GetModulesMethod(classDefinition, model);

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

    private void GetModulesMethod(ClassDefinition classDefinition, ModuleEntryPointModel model) {
        var loadDependenciesMethod = classDefinition.AddMethod("GetDependentModules");

        loadDependenciesMethod.InterfaceImplementation = KnownTypes.DependencyModules.Interfaces.IDependencyModule;
        loadDependenciesMethod.SetReturnType(TypeDefinition.Get(typeof(IEnumerable<object>)));

        foreach (var modelAttributeModel in model.AttributeModels) {
            if (modelAttributeModel.TypeDefinition.Name == "DependencyModuleAttribute") {
                continue;
            }
            var newStatement = New(modelAttributeModel.TypeDefinition, modelAttributeModel.Arguments);

            if (!string.IsNullOrEmpty(modelAttributeModel.PropertyAssignment)) {
                newStatement.AddInitValue(modelAttributeModel.PropertyAssignment);
            }

            loadDependenciesMethod.AddIndentedStatement(YieldReturn(newStatement));
        }

        if (loadDependenciesMethod.StatementCount == 0) {
            loadDependenciesMethod.AddIndentedStatement("yield break");
        }
    }

    private void ApplyServicesMethod(
        ClassDefinition classDefinition,
        ModuleEntryPointModel model) {

        var loadDependenciesMethod = classDefinition.AddMethod("ApplyServices");

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