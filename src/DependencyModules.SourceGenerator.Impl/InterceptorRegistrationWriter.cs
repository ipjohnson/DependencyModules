using DependencyModules.SourceGenerator.Impl.Utilities;
using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;
using static CSharpAuthor.SyntaxHelpers;

namespace DependencyModules.SourceGenerator.Impl;

/// <summary>
/// Registers each generated wrapper as a decorator of the service it intercepts, and registers the
/// interceptors themselves so the wrapper can be constructed.
/// </summary>
public class InterceptorRegistrationWriter {

    public string Write(
        ModuleEntryPointModel entryPointModel,
        DependencyModuleConfigurationModel configurationModel,
        IReadOnlyList<InterceptorModel> models) {

        var csharpFile = new CSharpFileDefinition(entryPointModel.EntryPointType.Namespace);

        var classDefinition = csharpFile.AddClass(entryPointModel.EntryPointType.Name);
        classDefinition.Modifiers |= ComponentModifier.Partial;

        for (var i = 0; i < models.Count; i++) {
            WriteInterceptor(entryPointModel, classDefinition, models[i], i, configurationModel);
        }

        var outputContext = new OutputContext(new OutputContextOptions {
            TypeOutputMode = TypeOutputMode.Global
        });

        csharpFile.WriteOutput(outputContext);

        return EntryModelUtil.ApplyRecordDeclaration(outputContext.Output(), entryPointModel);
    }

    private static void WriteInterceptor(
        ModuleEntryPointModel entryPointModel,
        ClassDefinition classDefinition,
        InterceptorModel model,
        int index,
        DependencyModuleConfigurationModel configurationModel) {

        var methodName = $"ApplyInterceptor{index}";

        var method = classDefinition.AddMethod(methodName);
        method.Modifiers |= ComponentModifier.Private | ComponentModifier.Static;

        if (configurationModel.ExcludeGeneratedCodeFromCoverage) {
            method.AddAttribute(TypeDefinition.Get("System.Diagnostics.CodeAnalysis", "ExcludeFromCodeCoverage"));
        }

        var services = method.AddParameter(
            KnownTypes.Microsoft.DependencyInjection.IServiceCollection, "services");

        // Each interceptor is registered as itself, not as IInterceptor. Registering the shared
        // interface instead made every interceptor visible to every wrapper, and two services with
        // different interceptors cross-applied each other's.
        //
        // TryAdd keeps a registration the developer made themselves, so an interceptor carrying its
        // own service attribute keeps that lifetime; services are applied before decorators, so
        // theirs is the one already in the collection.
        var registered = new HashSet<ITypeDefinition>();

        foreach (var interceptor in model.Interceptors) {
            if (!registered.Add(interceptor.Type)) {
                continue;
            }

            method.AddIndentedStatement(
                new StaticInvokeStatement(
                    KnownTypes.Microsoft.DependencyInjection.ServiceCollectionDescriptorExtensions,
                    "TryAddSingleton",
                    new List<IOutputComponent> {
                        CodeOutputComponent.Get(services.Name),
                        TypeOf(interceptor.Type)
                    }));
        }

        var wrapperName = $"{model.ImplementationType.Name.Replace(".", "_")}_Intercepted";
        var wrapperType = TypeDefinition.Get(model.ImplementationType.Namespace, wrapperName);

        // The wrapper is generated right here, so its constructor is known exactly: the intercepted
        // instance, then one parameter per interceptor. Emitting the `new` rather than handing the
        // type to ActivatorUtilities is what keeps interception working in a published Native AOT
        // application — the same reason decorators are emitted closed.
        var arguments = new List<object> { CodeOutputComponent.Get("inner") };

        for (var i = 0; i < model.Interceptors.Count; i++) {
            arguments.Add(
                new InvokeGenericDefinition(
                    "provider", "GetRequiredService", new[] { model.Interceptors[i].Type }));
        }

        method.NewLine();
        method.AddIndentedStatement(
            SyntaxHelpers.InvokeGeneric(
                KnownTypes.DependencyModules.Helpers.DecoratorHelper,
                "Decorate",
                new[] { model.ServiceType },
                CodeOutputComponent.Get(services.Name),
                TypeOf(wrapperType),
                new WrapStatement(
                    CodeOutputComponent.Get(" => "),
                    CodeOutputComponent.Get("(provider, inner)"),
                    New(wrapperType, arguments.ToArray()))));

        // A field initializer registers the method, matching how decorator registrations are hooked
        // up. DynamicDependency keeps the trimmer from removing a method only referenced this way.
        var field = classDefinition.AddField(typeof(int), $"interceptorField{index}");
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
                CodeOutputComponent.Get(model.Order.ToString())
            }) {
            Indented = false
        };
    }
}
