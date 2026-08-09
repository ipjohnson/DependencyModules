using System.Text.RegularExpressions;
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

        return RecordAware(outputContext.Output(), entryPointModel);
    }

    /// <summary>
    /// Rewrites the partial declaration when the module is a record.
    /// </summary>
    /// <remarks>
    /// CSharpAuthor has no record class, so every writer that contributes to a module's partial has
    /// to do this, and DependencyFileWriter and DependencyModuleWriter already did. This one did
    /// not, so a compilation holding both a record module and any decorator failed to build with
    /// CS0261 — the decorator file declared the module a class. It went unnoticed because a
    /// decorator file is only emitted once something in the compilation carries [Decorator].
    /// </remarks>
    private static string RecordAware(string output, ModuleEntryPointModel entryPointModel) {
        if (!entryPointModel.ModuleFeatures.HasFlag(ModuleEntryPointFeatures.IsRecord)) {
            return output;
        }

        return Regex.Replace(
            output,
            @"partial class " + Regex.Escape(entryPointModel.EntryPointType.Name) + @"(?!\w)",
            $"partial record class {entryPointModel.EntryPointType.Name}");
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

        method.AddIndentedStatement(
            new StaticInvokeStatement(
                KnownTypes.DependencyModules.Helpers.DecoratorHelper,
                "Decorate",
                new List<IOutputComponent> {
                    CodeOutputComponent.Get(services.Name),
                    TypeOf(decorator.ServiceType),
                    TypeOf(decorator.DecoratorType)
                }));

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
