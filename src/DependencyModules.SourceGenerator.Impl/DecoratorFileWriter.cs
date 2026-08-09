using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;
using DependencyModules.SourceGenerator.Impl.Utilities;
using static CSharpAuthor.SyntaxHelpers;

namespace DependencyModules.SourceGenerator.Impl;

/// <summary>
/// Emits the decorator registrations for one module.
/// </summary>
/// <remarks>
/// Each decorator gets its own method and its own registration, because each carries its own order.
/// The bodies are a single call into <c>DecoratorHelper</c>; the rewrite itself is deliberately not
/// generated.
/// </remarks>
public class DecoratorFileWriter {

    public string Write(
        ModuleEntryPointModel entryPointModel,
        DependencyModuleConfigurationModel configurationModel,
        IReadOnlyList<DecoratorModel> decorators) {

        var csharpFile = new CSharpFileDefinition(entryPointModel.EntryPointType.Namespace);

        var classDefinition = csharpFile.AddClass(entryPointModel.EntryPointType.Name);
        classDefinition.Modifiers |= ComponentModifier.Partial;

        // Applied per method rather than to the class. ExcludeFromCodeCoverage is not AllowMultiple,
        // and the same partial class also carries it from the registrations file.
        for (var i = 0; i < decorators.Count; i++) {
            WriteDecorator(entryPointModel, classDefinition, decorators[i], i, configurationModel);
        }

        var outputContext = new OutputContext(new OutputContextOptions {
            TypeOutputMode = TypeOutputMode.Global
        });

        csharpFile.WriteOutput(outputContext);

        return EntryModelUtil.ApplyRecordDeclaration(outputContext.Output(), entryPointModel);
    }


    private static void WriteDecorator(
        ModuleEntryPointModel entryPointModel,
        ClassDefinition classDefinition,
        DecoratorModel decorator,
        int index,
        DependencyModuleConfigurationModel configurationModel) {

        var methodName = $"ApplyDecorator{index}";

        var method = classDefinition.AddMethod(methodName);
        method.Modifiers |= ComponentModifier.Private | ComponentModifier.Static;

        if (configurationModel.ExcludeGeneratedCodeFromCoverage) {
            method.AddAttribute(TypeDefinition.Get("System.Diagnostics.CodeAnalysis", "ExcludeFromCodeCoverage"));
        }

        var services = method.AddParameter(
            KnownTypes.Microsoft.DependencyInjection.IServiceCollection, "services");

        // The parameter only appears when something tests it, so an unconditional decorator keeps
        // the RegistryFunc shape and the AddDecorator overload it always used.
        var hasConditions = decorator.Conditions is { Count: > 0 };

        var environment = hasConditions
            ? method.AddParameter(KnownTypes.DependencyModules.Interfaces.IModuleEnvironment, "environment")
            : null;

        var decorate = new StaticInvokeStatement(
            KnownTypes.DependencyModules.Helpers.DecoratorHelper,
            "Decorate",
            new List<IOutputComponent> {
                CodeOutputComponent.Get(services.Name),
                TypeOf(decorator.ServiceType),
                TypeOf(decorator.DecoratorType)
            });

        if (environment != null) {
            // Guarding the call rather than the registration: a decorator that does not apply is
            // simply not run, so the service resolves undecorated instead of being wrapped by
            // something that re-tests the environment on every call.
            var block = method.If(
                CodeOutputComponent.Get(
                    EnvironmentConditionWriter.BuildCondition(decorator.Conditions!, environment.Name)));

            block.AddIndentedStatement(decorate);
        } else {
            method.AddIndentedStatement(decorate);
        }

        // A field initializer registers the method, matching how service registrations are hooked up.
        // DynamicDependency keeps the trimmer from removing a method only referenced this way.
        var field = classDefinition.AddField(typeof(int), $"decoratorField{index}");
        field.Modifiers |= ComponentModifier.Private | ComponentModifier.Static;
        field.AddAttribute(
            TypeDefinition.Get("System.Diagnostics.CodeAnalysis", "DynamicDependency"),
            $"nameof({methodName})");

        var registryType = new GenericTypeDefinition(
            TypeDefinitionEnum.ClassDefinition,
            KnownTypes.DependencyModules.Helpers.Namespace,
            "DependencyRegistry",
            new[] { entryPointModel.EntryPointType });

        field.InitializeValue = new StaticInvokeStatement(
            registryType,
            "AddDecorator",
            new List<IOutputComponent> {
                CodeOutputComponent.Get(methodName),
                CodeOutputComponent.Get(decorator.Order.ToString())
            }) {
            Indented = false
        };
    }
}
