using System.Runtime.InteropServices;
using System.Text;
using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;
using DependencyModules.SourceGenerator.Impl.Utilities;
using static CSharpAuthor.SyntaxHelpers;

namespace DependencyModules.SourceGenerator.Impl;

public class DependencyFileWriter {
    private readonly FileLogger _logger;

    public DependencyFileWriter(FileLogger logger) {
        _logger = logger;
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

        return output.Output();
    }

    private void GenerateClass(ModuleEntryPointModel entryPointModel,
        DependencyModuleConfigurationModel configurationModel,
        IEnumerable<ServiceModel> serviceModels,
        CSharpFileDefinition csharpFile,
        string uniqueId) {

        var classDefinition = csharpFile.AddClass(entryPointModel.EntryPointType.Name);

        classDefinition.Modifiers |= ComponentModifier.Partial;

        var methodName =
            GenerateDependencyMethod(entryPointModel, configurationModel, serviceModels, classDefinition, uniqueId);

        CreateInvokeStatement(entryPointModel, methodName, classDefinition, uniqueId);
    }

    private void CreateInvokeStatement(ModuleEntryPointModel entryPointModel, string methodName, ClassDefinition classDefinition, string uniqueId) {
        var lowerName = uniqueId.ToLower() + "Field";

        var field = classDefinition.AddField(typeof(int), lowerName);

        field.Modifiers |= ComponentModifier.Private | ComponentModifier.Static;

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
        var services = method.AddParameter(KnownTypes.Microsoft.DependencyInjection.IServiceCollection, "services");

        var stringBuilder = new StringBuilder();

        var sortedServiceModels = GetSortedServiceModels(serviceModels);
        var autoRegisterGenerators =
            entryPointModel.RegisterJsonSerializers ?? configurationModel.RegisterSourceGenerator;

        foreach (var serviceModel in sortedServiceModels) {
            if (serviceModel.Equals(ServiceModel.Ignore)) {
                continue;
            }

            if ((serviceModel.Features & RegistrationFeature.AutoRegisterSourceGenerator) ==
                RegistrationFeature.AutoRegisterSourceGenerator && !autoRegisterGenerators) {
                continue;
            }

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
                            method,
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
                            method,
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
                    method,
                    services,
                    serviceModel,
                    uniqueId);
            }
        }

        return method.Name;
    }

    private void CrossWireRegisterImplementation(
        DependencyModuleConfigurationModel configurationModel,
        ModuleEntryPointModel entryPointModel,
        ClassDefinition classDefinition,
        MethodDefinition method,
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
            else if (serviceModel.Constructor != null &&
                     serviceModel.ImplementationType is not GenericTypeDefinition &&
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

        method.AddIndentedStatement(
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
        MethodDefinition method,
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
            else if (serviceModel.Constructor != null &&
                     serviceModel.ImplementationType is not GenericTypeDefinition &&
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

        method.AddIndentedStatement(
            services.Invoke(
                invokeMethod,
                serviceDescriptor
            ));
    }

    private static void HandleTryAndAddRegistrationTypes(DependencyModuleConfigurationModel configurationModel, ModuleEntryPointModel entryPointModel, ClassDefinition classDefinition, StringBuilder stringBuilder,
        RegistrationType registrationType,
        ServiceRegistrationModel registrationModel,
        ServiceModel serviceModel,
        MethodDefinition method,
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
            else if (serviceModel.Constructor != null &&
                     serviceModel.ImplementationType is not GenericTypeDefinition &&
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

        method.AddIndentedStatement(
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

    private static object[] GetArgumentsForParameterList(ParameterDefinition serviceProvider, IReadOnlyList<ParameterInfoModel> parameterList) {
        var returnList = new List<object>();

        foreach (var parameterInfoModel in parameterList) {
            var keyed = parameterInfoModel.Attributes.FirstOrDefault(
                a =>
                    a.TypeDefinition.Equals(KnownTypes.Microsoft.DependencyInjection.FromKeyedServicesAttribute));

            if (parameterInfoModel.ParameterType.Equals(KnownTypes.Microsoft.DependencyInjection.IServiceProvider)) {
                returnList.Add(serviceProvider);
            }
            else {
                var name = "Get";
                var parameters = new List<object>();

                if (!parameterInfoModel.ParameterType.IsNullable) {
                    name += "Required";
                }

                if (keyed != null) {
                    name += "Keyed";

                    var keyValue = keyed.Arguments.First().Value!;
                    if (keyValue is string stringValue) {
                        keyValue = QuoteString(stringValue);
                    }

                    parameters.Add(keyValue);
                }

                name += "Service";

                returnList.Add(
                    serviceProvider.InvokeGeneric(
                        name,
                        new[] {
                            parameterInfoModel.ParameterType.MakeNullable(false)
                        },
                        parameters.ToArray()
                    ));
            }
        }

        return returnList.ToArray();
    }

    private static RegistrationType GetRegistrationType(ModuleEntryPointModel entryPointModel, DependencyModuleConfigurationModel configurationModel, ServiceRegistrationModel registrationModel) {
        if (registrationModel.RegistrationType.HasValue) {
            return registrationModel.RegistrationType.Value;
        }

        if (entryPointModel.RegistrationType.HasValue) {
            return entryPointModel.RegistrationType.Value;
        }

        return configurationModel.RegistrationType;
    }

    private List<ServiceModel> GetSortedServiceModels(IEnumerable<ServiceModel> serviceModels) {
        var list = new List<ServiceModel>(serviceModels);

        list.Sort((x, y) =>
            string.Compare(x.ImplementationType.Name, y.ImplementationType.Name, StringComparison.Ordinal));

        return list;
    }
}