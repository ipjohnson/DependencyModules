using System.Runtime.InteropServices;
using System.Text;
using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;
using static CSharpAuthor.SyntaxHelpers;

namespace DependencyModules.SourceGenerator.Impl;

public class DependencyFileWriter {
    public string Write(
        ModuleEntryPointModel entryPointModel,
        DependencyModuleConfigurationModel configurationModel,
        IEnumerable<ServiceModel> serviceModels,
        string uniqueId) {
        var csharpFile = new CSharpFileDefinition(entryPointModel.EntryPointType.Namespace);

        GenerateClass(entryPointModel, configurationModel, serviceModels, csharpFile, uniqueId);

        var output = new OutputContext();

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
        IEnumerable<ServiceModel> serviceModels, ClassDefinition classDefinition, string uniqueId) {

        classDefinition.AddUsingNamespace("Microsoft.Extensions.DependencyInjection.Extensions");

        var method = classDefinition.AddMethod(uniqueId + "Dependencies");

        method.Modifiers |= ComponentModifier.Private | ComponentModifier.Static;
        var services = method.AddParameter(KnownTypes.Microsoft.DependencyInjection.IServiceCollection, "services");

        var stringBuilder = new StringBuilder();

        var sortedServiceModels = GetSortedServiceModels(serviceModels);

        foreach (var serviceModel in sortedServiceModels) {
            if (serviceModel.Equals(ServiceModel.Ignore)) {
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
                else if (entryPointModel.OnlyRealm) {
                    continue;
                }

                crossWire |= registrationModel.CrossWire.GetValueOrDefault(false);

                var registrationType = GetRegistrationType(entryPointModel, configurationModel, registrationModel);

                switch (registrationType) {
                    case RegistrationType.Add:
                    case RegistrationType.Try:
                        HandleTryAndAddRegistrationTypes(
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
                CrossWireRegisterImplementation(classDefinition, method, services, serviceModel, uniqueId);
            }
        }

        return method.Name;
    }

    private void CrossWireRegisterImplementation(
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
            parameters.Add(TypeOf(serviceModel.ImplementationType));
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

    private void HandleTryEnumerableAndReplaceRegistrationType(ClassDefinition classDefinition, RegistrationType registrationType,
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
            parameters.Add(TypeOf(serviceModel.ImplementationType));
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

    private static void HandleTryAndAddRegistrationTypes(ClassDefinition classDefinition, StringBuilder stringBuilder,
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
            parameters.Add(TypeOf(serviceModel.ImplementationType));
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

        var parameterList = new List<object>();

        foreach (var parameterInfoModel in factory.Parameters) {
            if (parameterInfoModel.ParameterType.Equals(KnownTypes.Microsoft.DependencyInjection.IServiceProvider)) {
                parameterList.Add(serviceProvider);
            }
            else {
                parameterList.Add(
                    serviceProvider.InvokeGeneric(
                        "GetService",
                        new[] {
                            parameterInfoModel.ParameterType.MakeNullable(false)
                        })
                );
            }
        }

        method.Return(Invoke(factory.TypeDefinition, factory.MethodName, parameterList.ToArray()));

        return method;
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