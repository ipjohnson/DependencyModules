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

    /// <summary>
    /// A type written as its unbound generic form — <c>IVault&lt;&gt;</c> rather than
    /// <c>IVault&lt;T&gt;</c>.
    /// </summary>
    /// <remarks>
    /// The only form a <c>typeof</c> can carry outside the type's own declaration. Writing the
    /// parameter names instead is CS0246, because no <c>T</c> is in scope at the registration.
    /// Blank-named arguments are how this codebase represents unbound throughout.
    /// </remarks>
    private static ITypeDefinition Unbound(ITypeDefinition type, int arity) {
        var arguments = new ITypeDefinition[arity];

        for (var i = 0; i < arity; i++) {
            arguments[i] = TypeDefinition.Get("", "");
        }

        return new GenericTypeDefinition(
            TypeDefinitionEnum.ClassDefinition, type.Namespace, type.Name, arguments);
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
                    TryAddMethodFor(interceptor.Lifestyle),
                    new List<IOutputComponent> {
                        CodeOutputComponent.Get(services.Name),
                        TypeOf(interceptor.Type)
                    }));
        }

        var wrapperName = $"{model.ImplementationType.Name.Replace(".", "_")}_Intercepted";
        var wrapperType = TypeDefinition.Get(model.ImplementationType.Namespace, wrapperName);

        method.NewLine();

        if (model.IsOpenGeneric) {
            // An open generic service cannot be decorated: decoration rewrites the registration into
            // a factory, and the container refuses a factory for one. It does accept an open generic
            // implementation type, and the wrapper is one — so the registration is swapped for the
            // wrapper and the implementation is registered under its own type for the wrapper to take.
            //
            // Nothing is closed here. The container closes the wrapper per requested construction, and
            // every type it names exists in the assembly, so this survives publishing as the closed
            // path does.
            method.AddIndentedStatement(
                new StaticInvokeStatement(
                    KnownTypes.DependencyModules.Helpers.DecoratorHelper,
                    "InterceptOpenGeneric",
                    new List<IOutputComponent> {
                        CodeOutputComponent.Get(services.Name),
                        TypeOf(Unbound(model.ServiceType, model.TypeParameters!.Count)),
                        TypeOf(Unbound(model.ImplementationType, model.TypeParameters!.Count)),
                        TypeOf(Unbound(wrapperType, model.TypeParameters!.Count))
                    }));
        } else {
            // The wrapper is generated right here, so its constructor is known exactly: the
            // intercepted instance, then one parameter per interceptor. Emitting the `new` rather than
            // handing the type to ActivatorUtilities is what keeps interception working in a published
            // Native AOT application — the same reason decorators are emitted closed.
            var arguments = new List<object> { CodeOutputComponent.Get("inner") };

            for (var i = 0; i < model.Interceptors.Count; i++) {
                arguments.Add(
                    new InvokeGenericDefinition(
                        "provider", "GetRequiredService", new[] { model.Interceptors[i].Type }));
            }

            // The implementation is named so the rewrite lands only on the registration this
            // wrapper was generated from. Without it a sibling implementation of the same interface
            // — one carrying no [Intercept] at all — came back wrapped in this class's wrapper, and
            // two intercepted implementations wrapped each other's registrations so every
            // interceptor ran twice per call.
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
                        New(wrapperType, arguments.ToArray())),
                    TypeOf(model.ImplementationType)));
        }

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

    /// <summary>
    /// The TryAdd overload for an interceptor's declared lifetime.
    /// </summary>
    /// <remarks>
    /// Still TryAdd whichever it is, so an interceptor carrying its own service attribute keeps the
    /// lifetime that attribute gave it - services are applied before decorators, so that
    /// registration is already in the collection by the time this runs.
    /// </remarks>
    private static string TryAddMethodFor(ServiceLifestyle lifestyle) =>
        lifestyle switch {
            ServiceLifestyle.Scoped => "TryAddScoped",
            ServiceLifestyle.Transient => "TryAddTransient",
            _ => "TryAddSingleton"
        };
}
