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

    /// <param name="uniqueId">
    /// Distinguishes the methods and fields this file declares from those another file declares on
    /// the same partial class. The attribute path and the convention path each emit decorations for
    /// their own registrations, into two files and one class, so unprefixed names would be CS0102.
    /// </param>
    public string Write(
        ModuleEntryPointModel entryPointModel,
        DependencyModuleConfigurationModel configurationModel,
        IReadOnlyList<DecoratorModel> decorators,
        string uniqueId = "") {

        var csharpFile = new CSharpFileDefinition(entryPointModel.EntryPointType.Namespace);

        var classDefinition = csharpFile.AddClass(entryPointModel.EntryPointType.Name);
        classDefinition.Modifiers |= ComponentModifier.Partial;

        // Applied per method rather than to the class. ExcludeFromCodeCoverage is not AllowMultiple,
        // and the same partial class also carries it from the registrations file.
        // Anything that cannot be constructed by generated code has already been reported and
        // dropped. There is no reflective shape left to fall back to, so reaching the writer means
        // the decoration can be emitted.
        for (var i = 0; i < decorators.Count; i++) {
            WriteDecorator(entryPointModel, classDefinition, decorators[i], i, configurationModel, uniqueId);
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
        DependencyModuleConfigurationModel configurationModel,
        string uniqueId) {

        var methodName = $"Apply{uniqueId}Decorator{index}";

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

        var decorate = Decoration(decorator, services.Name, decorator.ServiceType, decorator.DecoratorType);

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

        WriteRegistration(entryPointModel, classDefinition, decorator, index, methodName, uniqueId);
    }

    /// <summary>
    /// One decoration, as a closed call constructing the decorator inline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The service is a type argument and the decorator is a literal <c>new</c>, so everything the
    /// decoration needs exists in the emitted assembly. The shape this replaced —
    /// <c>Decorate(services, typeof(IFoo), typeof(FooDecorator))</c> — left the construction to
    /// <c>ActivatorUtilities</c>, and for a generic decorator to <c>Type.MakeGenericType</c>. Both
    /// work under a JIT and neither survives publishing: measured, every decorator failed under
    /// Native AOT, the non-generic one because the trimmer had no reason to keep a constructor
    /// nothing named, and the generic one because no instantiation was statically reachable.
    /// </para>
    /// <para>
    /// <paramref name="serviceType"/> and <paramref name="decoratorType"/> are passed rather than
    /// read off the model so a generic decorator can be emitted once per closed registration, with
    /// both closed over the same arguments.
    /// </para>
    /// </remarks>
    private static IOutputComponent Decoration(
        DecoratorModel decorator,
        string servicesName,
        ITypeDefinition serviceType,
        ITypeDefinition decoratorType) {

        // Shared with the service writer rather than reimplemented. Resolving every parameter with
        // GetRequiredService looked right and quietly ignored what the parameter declared: a
        // [FromKeyedServices] dependency resolved the unkeyed registration, and a nullable one threw
        // instead of resolving to null.
        var arguments = ConstructorArgumentWriter.Arguments(
            new ParameterDefinition(
                KnownTypes.Microsoft.DependencyInjection.IServiceProvider, ProviderParameterName),
            decorator.Constructor!.Parameters,
            decorator.InnerParameterIndex,
            CodeOutputComponent.Get(InnerParameterName));

        var construct = New(decoratorType, arguments);

        var lambda = new WrapStatement(
            CodeOutputComponent.Get(" => "),
            CodeOutputComponent.Get($"({ProviderParameterName}, {InnerParameterName})"),
            construct);

        // The four-argument overload when an implementation is named, which is the one interception
        // already uses: it skips a descriptor whose origin is a different type, so the decorator
        // reaches one registration rather than every registration of the service.
        if (decorator.Implementation != null) {
            return SyntaxHelpers.InvokeGeneric(
                KnownTypes.DependencyModules.Helpers.DecoratorHelper,
                "Decorate",
                new[] { serviceType },
                CodeOutputComponent.Get(servicesName),
                TypeOf(decoratorType),
                lambda,
                TypeOf(decorator.Implementation));
        }

        return SyntaxHelpers.InvokeGeneric(
            KnownTypes.DependencyModules.Helpers.DecoratorHelper,
            "Decorate",
            new[] { serviceType },
            CodeOutputComponent.Get(servicesName),
            TypeOf(decoratorType),
            lambda);
    }

    private static string ToCamel(string value) =>
        string.IsNullOrEmpty(value) ? "" : char.ToLowerInvariant(value[0]) + value.Substring(1);

    private const string ProviderParameterName = "provider";

    private const string InnerParameterName = "inner";

    private static void WriteRegistration(
        ModuleEntryPointModel entryPointModel,
        ClassDefinition classDefinition,
        DecoratorModel decorator,
        int index,
        string methodName,
        string uniqueId) {

        // A field initializer registers the method, matching how service registrations are hooked up.
        // DynamicDependency keeps the trimmer from removing a method only referenced this way.
        var field = classDefinition.AddField(typeof(int), $"{ToCamel(uniqueId)}decoratorField{index}");
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
