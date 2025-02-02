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

        var methodName = GenerateDependencyMethod(entryPointModel, configurationModel, serviceModels, classDefinition, uniqueId);

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


                var registrationType = GetRegistrationType(entryPointModel, configurationModel, registrationModel);

                switch (registrationType) {
                    case RegistrationType.Add:
                    case RegistrationType.Try:
                        HandleTryAndAddRegistrationTypes(
                            stringBuilder, registrationType, registrationModel, serviceModel, method, services);
                        break;
                    
                    case RegistrationType.Replace:
                    case RegistrationType.TryEnumerable:
                        HandleTryEnumerableAndReplaceRegistrationType(
                            registrationType, registrationModel, serviceModel, method, services);
                        
                        break;
                }
            }
        }

        return method.Name;
    }

    private void HandleTryEnumerableAndReplaceRegistrationType(
        RegistrationType registrationType,
        ServiceRegistrationModel registrationModel, 
        ServiceModel serviceModel,
        MethodDefinition method,
        ParameterDefinition services) {
        var invokeMethod = 
            registrationType == RegistrationType.Replace ? "Replace" : "TryAddEnumerable";

        var parameters = new List<object> {
            TypeOf(registrationModel.ServiceType)
        };
        
        if (registrationModel.Key != null) {
            parameters.Add(registrationModel.Key);
        }
        parameters.Add(TypeOf(serviceModel.ImplementationType));

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

    private static void HandleTryAndAddRegistrationTypes(
        StringBuilder stringBuilder, RegistrationType registrationType, ServiceRegistrationModel registrationModel, ServiceModel serviceModel,
        MethodDefinition method,
        ParameterDefinition services) {
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

        parameters.Add(TypeOf(serviceModel.ImplementationType));

        method.AddIndentedStatement(
            services.Invoke(
                stringBuilder.ToString(),
                parameters.ToArray()
            ));
    }

    private static RegistrationType GetRegistrationType(ModuleEntryPointModel entryPointModel, DependencyModuleConfigurationModel configurationModel, ServiceRegistrationModel registrationModel) {
        if (registrationModel.RegistrationType.HasValue) {
            return registrationModel.RegistrationType.Value;
        }

        if (entryPointModel.UseTry.HasValue) {
            return RegistrationType.Try;
        }

        return configurationModel.DefaultUseTry ? RegistrationType.Try : RegistrationType.Add;
    }

    private List<ServiceModel> GetSortedServiceModels(IEnumerable<ServiceModel> serviceModels) {
        var list = new List<ServiceModel>(serviceModels);

        list.Sort((x, y) =>
            string.Compare(x.ImplementationType.Name, y.ImplementationType.Name, StringComparison.Ordinal));

        return list;
    }
}