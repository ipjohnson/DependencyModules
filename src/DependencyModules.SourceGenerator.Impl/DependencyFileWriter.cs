using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;
using DependencyModules.SourceGenerator.Impl.Utilities;
using static CSharpAuthor.SyntaxHelpers;

namespace DependencyModules.SourceGenerator.Impl;

public class DependencyFileWriter {
    private readonly FileLogger _logger;
    private readonly bool _coverageAttributeOnMethod;

    /// <param name="logger">Receives the discovery log.</param>
    /// <param name="coverageAttributeOnMethod">
    /// Puts ExcludeFromCodeCoverage on the generated method instead of on the partial class.
    ///
    /// ExcludeFromCodeCoverage is not AllowMultiple, and attributes on partial parts combine, so two
    /// generated parts of one module each carrying it at class level is CS0579. Only one writer can
    /// own the class-level attribute; every other file contributing to the same partial has to apply
    /// it per member, which is what DecoratorFileWriter already does.
    /// </param>
    public DependencyFileWriter(FileLogger logger, bool coverageAttributeOnMethod = false) {
        _logger = logger;
        _coverageAttributeOnMethod = coverageAttributeOnMethod;
    }

    public string Write(
        ModuleEntryPointModel entryPointModel,
        DependencyModuleConfigurationModel configurationModel,
        IEnumerable<ServiceModel> serviceModels,
        string uniqueId) {

        if (entryPointModel.ModuleFeatures.HasFlag(ModuleEntryPointFeatures.AutoGenerateModule) &&
            string.IsNullOrEmpty(entryPointModel.EntryPointType.Namespace)) {
            entryPointModel = entryPointModel with {
                EntryPointType = TypeDefinition.Get(configurationModel.RootNamespace, entryPointModel.EntryPointType.Name)
            };
        }

        _logger.Info($"Generating Dependencies for {entryPointModel.EntryPointType.Namespace}.{entryPointModel.EntryPointType.Namespace}");

        var csharpFile = new CSharpFileDefinition(entryPointModel.EntryPointType.Namespace);

        GenerateClass(entryPointModel, configurationModel, serviceModels, csharpFile, uniqueId);

        var output = new OutputContext(
            new OutputContextOptions {
                TypeOutputMode = TypeOutputMode.Global
            });

        csharpFile.WriteOutput(output);

        var result = output.Output();

        result = EntryModelUtil.ApplyRecordDeclaration(result, entryPointModel);

        return result;
    }

    private void GenerateClass(ModuleEntryPointModel entryPointModel,
        DependencyModuleConfigurationModel configurationModel,
        IEnumerable<ServiceModel> serviceModels,
        CSharpFileDefinition csharpFile,
        string uniqueId) {

        var classDefinition = csharpFile.AddClass(entryPointModel.EntryPointType.Name);

        classDefinition.Modifiers |= ComponentModifier.Partial;

        if (configurationModel.ExcludeGeneratedCodeFromCoverage && !_coverageAttributeOnMethod) {
            classDefinition.AddAttribute(
                TypeDefinition.Get("System.Diagnostics.CodeAnalysis", "ExcludeFromCodeCoverage"));
        }

        var methodName =
            GenerateDependencyMethod(entryPointModel, configurationModel, serviceModels, classDefinition, uniqueId);

        CreateInvokeStatement(entryPointModel, methodName, classDefinition, uniqueId);
    }

    private void CreateInvokeStatement(ModuleEntryPointModel entryPointModel, string methodName, ClassDefinition classDefinition, string uniqueId) {
        var lowerName = uniqueId.ToLower() + "Field";

        var field = classDefinition.AddField(typeof(int), lowerName);

        field.Modifiers |= ComponentModifier.Private | ComponentModifier.Static;
        field.AddAttribute(TypeDefinition.Get("System.Diagnostics.CodeAnalysis", "DynamicDependency"), $"nameof({methodName})");

        var closedType = new GenericTypeDefinition(
            TypeDefinitionEnum.ClassDefinition, KnownTypes.DependencyModules.Helpers.Namespace, "DependencyRegistry", new[] {
                entryPointModel.EntryPointType
            });

        var invokeStatement = new StaticInvokeStatement(closedType, "Add", new List<IOutputComponent> {
            CodeOutputComponent.Get(methodName)
        }) {
            Indented = false
        };

        field.InitializeValue = invokeStatement;
    }

    private string GenerateDependencyMethod(ModuleEntryPointModel entryPointModel,
        DependencyModuleConfigurationModel configurationModel,
        IEnumerable<ServiceModel> serviceModels,
        ClassDefinition classDefinition,
        string uniqueId) {

        classDefinition.AddUsingNamespace("Microsoft.Extensions.DependencyInjection.Extensions");

        var method = classDefinition.AddMethod(uniqueId + "Dependencies");

        method.Modifiers |= ComponentModifier.Private | ComponentModifier.Static;

        // The glue factories GenerateGlueFactory may add are not covered here. They exist only for
        // [Factory] registrations, which the attribute path produces and the convention path — the
        // only caller that sets this — cannot.
        if (configurationModel.ExcludeGeneratedCodeFromCoverage && _coverageAttributeOnMethod) {
            method.AddAttribute(
                TypeDefinition.Get("System.Diagnostics.CodeAnalysis", "ExcludeFromCodeCoverage"));
        }
        var services = method.AddParameter(KnownTypes.Microsoft.DependencyInjection.IServiceCollection, "services");

        var stringBuilder = new StringBuilder();

        var sortedServiceModels = GetSortedServiceModels(serviceModels, configurationModel);
        var autoRegisterGenerators =
            entryPointModel.RegisterJsonSerializers ?? configurationModel.RegisterSourceGenerator;

        // The parameter is added only when something in this module is conditional, so a module
        // without conditions generates exactly the method it always has and its Add call still
        // binds to the RegistryFunc overload.
        var environment = sortedServiceModels.Any(model => model.Conditions is { Count: > 0 })
            ? method.AddParameter(KnownTypes.DependencyModules.Interfaces.IModuleEnvironment, "environment")
            : null;

        foreach (var serviceModel in sortedServiceModels) {
            if (serviceModel.Equals(ServiceModel.Ignore)) {
                continue;
            }

            if ((serviceModel.Features & RegistrationFeature.AutoRegisterSourceGenerator) ==
                RegistrationFeature.AutoRegisterSourceGenerator && !autoRegisterGenerators) {
                continue;
            }

            // One guard around everything the service registers. The attributes are declared on the
            // class, so every registration it produces shares them.
            var block = environment != null && serviceModel.Conditions is { Count: > 0 } conditions
                ? method.If(CodeOutputComponent.Get(BuildCondition(conditions, environment.Name)))
                : (BaseBlockDefinition)method;

            var crossWire = false;

            foreach (var registrationModel in serviceModel.Registrations) {
                // skip registrations not for this realm
                if (registrationModel.Realm != null) {
                    if (!registrationModel.Realm.Equals(entryPointModel.EntryPointType)) {
                        continue;
                    }
                }
                else if (
                    (entryPointModel.ModuleFeatures & ModuleEntryPointFeatures.OnlyRealm) ==
                    ModuleEntryPointFeatures.OnlyRealm) {
                    continue;
                }

                if (registrationModel.Namespaces != null) {
                    foreach (var namespaceString in registrationModel.Namespaces) {
                        classDefinition.AddUsingNamespace(namespaceString);
                    }
                }

                crossWire |= registrationModel.CrossWire.GetValueOrDefault(false);

                var registrationType = GetRegistrationType(entryPointModel, configurationModel, registrationModel);

                switch (registrationType) {
                    case RegistrationType.Add:
                    case RegistrationType.Try:
                        HandleTryAndAddRegistrationTypes(
                            configurationModel,
                            entryPointModel,
                            classDefinition,
                            stringBuilder,
                            registrationType,
                            registrationModel,
                            serviceModel,
                            block,
                            services,
                            uniqueId);
                        break;

                    case RegistrationType.Replace:
                    case RegistrationType.TryEnumerable:
                        HandleTryEnumerableAndReplaceRegistrationType(
                            configurationModel,
                            entryPointModel,
                            classDefinition,
                            registrationType,
                            registrationModel,
                            serviceModel,
                            block,
                            services,
                            uniqueId);

                        break;
                }
            }

            if (crossWire) {
                CrossWireRegisterImplementation(
                    configurationModel,
                    entryPointModel,
                    classDefinition,
                    block,
                    services,
                    serviceModel,
                    uniqueId);
            }
        }

        return method.Name;
    }

    // Shared with DecoratorFileWriter through EnvironmentConditionWriter, so a service and a
    // decorator carrying the same attributes cannot end up testing them differently.
    private static string BuildCondition(
        IReadOnlyList<EnvironmentConditionModel> conditions, string environmentParameter) =>
        EnvironmentConditionWriter.BuildCondition(conditions, environmentParameter);

    private void CrossWireRegisterImplementation(
        DependencyModuleConfigurationModel configurationModel,
        ModuleEntryPointModel entryPointModel,
        ClassDefinition classDefinition,
        BaseBlockDefinition block,
        ParameterDefinition services,
        ServiceModel serviceModel,
        string uniqueId) {
        var registrationModel =
            serviceModel.Registrations.First(r => r.CrossWire.GetValueOrDefault(false));

        var invokeMethod = "";
        switch (registrationModel.RegistrationType.GetValueOrDefault(RegistrationType.Add)) {
            case RegistrationType.Add:
                invokeMethod = "Add";
                break;
            case RegistrationType.Try:
                invokeMethod = "Try";
                break;
            case RegistrationType.Replace:
                invokeMethod = "Replace";
                break;
            case RegistrationType.TryEnumerable:
                invokeMethod = "TryEnumerable";
                break;
        }

        var parameters = new List<object> {
            TypeOf(serviceModel.ImplementationType)
        };

        if (registrationModel.Key != null) {
            parameters.Add(registrationModel.Key);
        }

        if (serviceModel.Factory == null) {
            if (serviceModel.FactoryOutput != null) {
                parameters.Add(serviceModel.FactoryOutput);
            }
            else if (serviceModel is { Constructor: not null, ImplementationType: not GenericTypeDefinition } &&
                     entryPointModel.GenerateFactories.GetValueOrDefault(
                         configurationModel.GenerateFactories)) {
                parameters.Add(GenerateNewFactory(serviceModel, registrationModel));
            }
            else {
                parameters.Add(TypeOf(serviceModel.ImplementationType));
            }
        }
        else {
            AddFactoryParameter(serviceModel, classDefinition, parameters, uniqueId);
        }

        switch (registrationModel.Lifestyle) {
            case ServiceLifestyle.Transient:
                parameters.Add(CodeOutputComponent.Get("ServiceLifetime.Transient"));
                break;
            case ServiceLifestyle.Scoped:
                parameters.Add(CodeOutputComponent.Get("ServiceLifetime.Scoped"));
                break;
            case ServiceLifestyle.Singleton:
                parameters.Add(CodeOutputComponent.Get("ServiceLifetime.Singleton"));
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        var serviceDescriptor =
            New(
                KnownTypes.Microsoft.DependencyInjection.ServiceDescriptor,
                parameters.ToArray());

        block.AddIndentedStatement(
            services.Invoke(
                invokeMethod,
                serviceDescriptor
            ));
    }

    private static object GenerateNewFactory(ServiceModel serviceModel, ServiceRegistrationModel registrationModel) {
        var parameter =
            new ParameterDefinition(KnownTypes.Microsoft.DependencyInjection.IServiceProvider, "provider");

        var providerParameters = registrationModel.Key == null ? "provider => " : "(provider, _) => ";
        var provider = CodeOutputComponent.Get(providerParameters);

        var newStatement = New(
            serviceModel.ImplementationType,
            GetArgumentsForParameterList(parameter, serviceModel.Constructor!.Parameters));

        return new WrapStatement(newStatement, provider, null);
    }

    private void HandleTryEnumerableAndReplaceRegistrationType(DependencyModuleConfigurationModel configurationModel, ModuleEntryPointModel entryPointModel, ClassDefinition classDefinition,
        RegistrationType registrationType,
        ServiceRegistrationModel registrationModel,
        ServiceModel serviceModel,
        BaseBlockDefinition block,
        ParameterDefinition services, string uniqueId) {
        var invokeMethod =
            registrationType == RegistrationType.Replace ? "Replace" : "TryAddEnumerable";

        var parameters = new List<object> {
            TypeOf(registrationModel.ServiceType)
        };

        if (registrationModel.Key != null) {
            parameters.Add(registrationModel.Key);
        }

        if (registrationModel.CrossWire == true) {
            AddCrossWireParameter(serviceModel, registrationModel, parameters);
        }
        else if (serviceModel.Factory == null) {
            if (serviceModel.FactoryOutput != null) {
                var factoryOutput = serviceModel.FactoryOutput?.Invoke(serviceModel, registrationModel);

                parameters.Add(factoryOutput ?? TypeOf(serviceModel.ImplementationType));
            }
            else if (serviceModel is { Constructor: not null, ImplementationType: not GenericTypeDefinition } &&
                     entryPointModel.GenerateFactories.GetValueOrDefault(
                         configurationModel.GenerateFactories)) {
                parameters.Add(GenerateNewFactory(serviceModel, registrationModel));
            }
            else {
                parameters.Add(TypeOf(serviceModel.ImplementationType));
            }
        }
        else {
            AddFactoryParameter(serviceModel, classDefinition, parameters, uniqueId);
        }

        switch (registrationModel.Lifestyle) {
            case ServiceLifestyle.Transient:
                parameters.Add(CodeOutputComponent.Get("ServiceLifetime.Transient"));
                break;
            case ServiceLifestyle.Scoped:
                parameters.Add(CodeOutputComponent.Get("ServiceLifetime.Scoped"));
                break;
            case ServiceLifestyle.Singleton:
                parameters.Add(CodeOutputComponent.Get("ServiceLifetime.Singleton"));
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        var serviceDescriptor =
            New(
                KnownTypes.Microsoft.DependencyInjection.ServiceDescriptor,
                parameters.ToArray());

        block.AddIndentedStatement(
            services.Invoke(
                invokeMethod,
                serviceDescriptor
            ));
    }

    private static void HandleTryAndAddRegistrationTypes(DependencyModuleConfigurationModel configurationModel, ModuleEntryPointModel entryPointModel, ClassDefinition classDefinition, StringBuilder stringBuilder,
        RegistrationType registrationType,
        ServiceRegistrationModel registrationModel,
        ServiceModel serviceModel,
        BaseBlockDefinition block,
        ParameterDefinition services,
        string uniqueId) {
        stringBuilder.Length = 0;

        if (registrationType == RegistrationType.Try) {
            stringBuilder.Append("Try");
        }

        stringBuilder.Append("Add");

        if (registrationModel.Key != null) {
            stringBuilder.Append("Keyed");
        }

        switch (registrationModel.Lifestyle) {
            case ServiceLifestyle.Transient:
                stringBuilder.Append("Transient");
                break;
            case ServiceLifestyle.Scoped:
                stringBuilder.Append("Scoped");
                break;
            case ServiceLifestyle.Singleton:
                stringBuilder.Append("Singleton");
                break;
        }

        var parameters = new List<object>();

        parameters.Add(TypeOf(registrationModel.ServiceType));

        if (registrationModel.Key != null) {
            parameters.Add(registrationModel.Key);
        }

        if (registrationModel.CrossWire == true) {
            AddCrossWireParameter(
                serviceModel, registrationModel, parameters);
        }
        else if (serviceModel.Factory == null) {
            if (serviceModel.FactoryOutput != null) {
                var factoryOutput = serviceModel.FactoryOutput?.Invoke(serviceModel, registrationModel);

                parameters.Add(factoryOutput ?? TypeOf(serviceModel.ImplementationType));
            }
            else if (serviceModel is { Constructor: not null, ImplementationType: not GenericTypeDefinition } &&
                     entryPointModel.GenerateFactories.GetValueOrDefault(
                         configurationModel.GenerateFactories)) {
                parameters.Add(GenerateNewFactory(serviceModel, registrationModel));
            }
            else {
                parameters.Add(TypeOf(serviceModel.ImplementationType));
            }
        }
        else {
            AddFactoryParameter(serviceModel, classDefinition, parameters, uniqueId);
        }

        block.AddIndentedStatement(
            services.Invoke(
                stringBuilder.ToString(),
                parameters.ToArray()
            ));
    }

    private static void AddCrossWireParameter(
        ServiceModel serviceModel,
        ServiceRegistrationModel registrationModel,
        List<object> parameters) {
        IOutputComponent invoke;

        var serviceProvider =
            new ParameterDefinition(KnownTypes.Microsoft.DependencyInjection.IServiceProvider, "s");

        if (registrationModel.Key != null) {
            var key = registrationModel.Key;

            if (key is string stringValue) {
                key = QuoteString(stringValue);
            }

            invoke =
                serviceProvider.InvokeGeneric(
                    "GetRequiredKeyedServices",
                    new[] {
                        serviceModel.ImplementationType
                    },
                    key);
        }
        else {
            invoke =
                serviceProvider.InvokeGeneric("GetRequiredService", new[] {
                    serviceModel.ImplementationType
                });
        }

        var wrapper = new WrapStatement(CodeOutputComponent.Get(" => "), serviceProvider, invoke);

        parameters.Add(wrapper);
    }

    private static void AddFactoryParameter(ServiceModel serviceModel, ClassDefinition classDefinition, List<object> parameters, string uniqueId) {
        var factory = serviceModel.Factory;
        if (factory == null) {
            return;
        }

        if (factory.Parameters.Count == 1 && factory.Parameters.Any(m =>
                m.ParameterType.Equals(KnownTypes.Microsoft.DependencyInjection.IServiceProvider))) {
            parameters.Add(CodeOutputComponent.Get(
                factory.TypeDefinition.Namespace + "." + factory.TypeDefinition.Name + "." + factory.MethodName));
        }
        else {
            var glueFactory = GenerateGlueFactory(
                serviceModel, factory, classDefinition, uniqueId);

            parameters.Add(CodeOutputComponent.Get(glueFactory.Name));
        }
    }

    private static MethodDefinition GenerateGlueFactory(
        ServiceModel serviceModel,
        ServiceFactoryModel factory,
        ClassDefinition classDefinition,
        string uniqueId) {
        var glueFactoryName = uniqueId + "GlueFactory" + classDefinition.Methods.Count;
        var method = classDefinition.AddMethod(glueFactoryName);

        method.Modifiers |= ComponentModifier.Private | ComponentModifier.Static;
        method.SetReturnType(serviceModel.ImplementationType);

        var serviceProvider = method.AddParameter(
            KnownTypes.Microsoft.DependencyInjection.IServiceProvider, "serviceProvider");

        var parameterList = GetArgumentsForParameterList(serviceProvider, factory.Parameters);

        method.Return(Invoke(factory.TypeDefinition, factory.MethodName, parameterList.ToArray()));

        return method;
    }

    private static object[] GetArgumentsForParameterList(
        ParameterDefinition serviceProvider, IReadOnlyList<ParameterInfoModel> parameterList) =>
        ConstructorArgumentWriter.Arguments(serviceProvider, parameterList);

    private static RegistrationType GetRegistrationType(ModuleEntryPointModel entryPointModel, DependencyModuleConfigurationModel configurationModel, ServiceRegistrationModel registrationModel) {
        if (registrationModel.RegistrationType.HasValue) {
            return registrationModel.RegistrationType.Value;
        }

        if (entryPointModel.RegistrationType.HasValue) {
            return entryPointModel.RegistrationType.Value;
        }

        return configurationModel.RegistrationType;
    }

    /// <summary>
    /// Emission order: unconditional registrations first, then conditional ones, each group by name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The container resolves a single service from the <b>last</b> matching descriptor, so a
    /// conditional registration can only override an unconditional default for the same service type
    /// if it is emitted after it. Ordering by name alone made that depend on the class names: a
    /// <c>[IfEnvironment("Development")] FakeEmailSender</c> sorts before an unconditional
    /// <c>SmtpEmailSender</c>, so the default landed last and won in every environment.
    /// </para>
    /// <para>
    /// This orders within one module. A default and its override living in different modules are
    /// ordered by the sequence the modules are applied in, which is the caller's.
    /// </para>
    /// <para>
    /// Note the interaction with <c>RegistrationType.Try</c>, which is first-wins rather than
    /// last-wins: a conditional <c>Try</c> registration cannot override an unconditional one,
    /// because by the time it runs the service type is already registered. That is what <c>Try</c>
    /// means, and the override pattern wants the default <c>Add</c>.
    /// </para>
    /// <para>
    /// <c>Try</c> and <c>Replace</c> are ordered after plain <c>Add</c> within each group for the same
    /// reason the conditional key exists: both act <i>on</i> a registration that has to already be
    /// there. Ordered by name alone, whether they worked depended on how the two classes happened to
    /// be named — a <c>Replace</c> emitted before its target replaced nothing, added itself, and was
    /// then beaten by the very registration it meant to displace. Renaming the class fixed it, and
    /// nothing said so.
    /// </para>
    /// </remarks>
    private List<ServiceModel> GetSortedServiceModels(
        IEnumerable<ServiceModel> serviceModels, DependencyModuleConfigurationModel configurationModel) {

        var list = new List<ServiceModel>(serviceModels);

        list.Sort((x, y) => {
            var byCondition = IsConditional(x).CompareTo(IsConditional(y));

            if (byCondition != 0) {
                return byCondition;
            }

            var byStrategy = ActsOnExistingRegistration(x, configurationModel)
                .CompareTo(ActsOnExistingRegistration(y, configurationModel));

            // Name is the tie-break rather than the only key, so the order stays total and the
            // output stays deterministic under List.Sort, which is not stable.
            return byStrategy != 0
                ? byStrategy
                : string.Compare(x.ImplementationType.Name, y.ImplementationType.Name, StringComparison.Ordinal);
        });

        return list;
    }

    private static bool IsConditional(ServiceModel serviceModel) =>
        serviceModel.Conditions is { Count: > 0 };

    /// <summary>
    /// Whether any of a service's registrations only makes sense once its service type is registered.
    /// </summary>
    /// <remarks>
    /// <c>TryEnumerable</c> is deliberately not here. It skips only an identical service-and-
    /// implementation pair, so several implementations of one service all register whatever order
    /// they arrive in, and deferring it would change nothing.
    /// </remarks>
    private static bool ActsOnExistingRegistration(
        ServiceModel serviceModel, DependencyModuleConfigurationModel configurationModel) {

        foreach (var registration in serviceModel.Registrations) {
            // Null means the registration took the project-wide default, which is what
            // DependencyModules_RegistrationType sets.
            var registrationType = registration.RegistrationType ?? configurationModel.RegistrationType;

            if (registrationType is RegistrationType.Try or RegistrationType.Replace) {
                return true;
            }
        }

        return false;
    }
}