using System.Text;
using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;
using DependencyModules.SourceGenerator.Impl.Models;
using Microsoft.CodeAnalysis;

namespace DependencyModules.SourceGenerator.Impl;


public class DependencyFileWriter {
    public string Write(
        ModuleEntryPointModel entryPointModel, 
        IEnumerable<ServiceModel> serviceModels,
        string uniqueId) {
        
        var csharpFile = new CSharpFileDefinition(entryPointModel.EntryPointType.Namespace);

        GenerateClass(entryPointModel, serviceModels, csharpFile, uniqueId);
        
        var output = new OutputContext();
        
        csharpFile.WriteOutput(output);
        
        return output.Output();
    }

    private void GenerateClass(
        ModuleEntryPointModel entryPointModel, 
        IEnumerable<ServiceModel> serviceModels, 
        CSharpFileDefinition csharpFile, 
        string uniqueId) {
        
        var classDefinition = csharpFile.AddClass(entryPointModel.EntryPointType.Name);

        classDefinition.Modifiers |= ComponentModifier.Partial;

        var methodName = GenerateDependencyMethod(entryPointModel, serviceModels, classDefinition, uniqueId);
        
        CreateInvokeStatement(entryPointModel, methodName, classDefinition, uniqueId);
    }

    private void CreateInvokeStatement(ModuleEntryPointModel entryPointModel, string methodName, ClassDefinition classDefinition, string uniqueId) {
        var lowerName = uniqueId.ToLower() + "Field";
        
        var field = classDefinition.AddField(typeof(int), lowerName);
        
        field.Modifiers |= ComponentModifier.Private | ComponentModifier.Static;
        
        var closedType = new GenericTypeDefinition(
            TypeDefinitionEnum.ClassDefinition, KnownTypes.DependencyModules.Helpers.Namespace, "DependencyRegistry", new []{entryPointModel.EntryPointType});

        var invokeStatement = new StaticInvokeStatement(closedType, "Add", new List<IOutputComponent> {
            CodeOutputComponent.Get(methodName)
        }) { Indented = false };

        field.InitializeValue = invokeStatement;
    }

    private string GenerateDependencyMethod(ModuleEntryPointModel entryPointModel, 
        IEnumerable<ServiceModel> serviceModels, ClassDefinition classDefinition, string uniqueId) {
        
        classDefinition.AddUsingNamespace("Microsoft.Extensions.DependencyInjection.Extensions");
        
        var method = classDefinition.AddMethod(uniqueId + "Dependencies");
        
        method.Modifiers |= ComponentModifier.Private | ComponentModifier.Static;
        var services = method.AddParameter(KnownTypes.Microsoft.DependencyInjection.IServiceCollection, "services");

        var stringBuilder = new StringBuilder();
        
        foreach (var serviceModel in serviceModels) {
            foreach (var registrationModel in serviceModel.Registrations) {
                // skip registrations not for this realm
                if (registrationModel.Realm != null) {
                    if (!registrationModel.Realm.Equals(entryPointModel.EntryPointType)) {
                        continue;
                    }
                } else if (entryPointModel.OnlyRealm) {
                    continue;
                }

                stringBuilder.Length = 0;

                if (registrationModel.RegisterWithTry) {
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

                var output = new List<object>();

                output.Add(TypeOf(registrationModel.ServiceType));

                if (registrationModel.Key != null) {
                    output.Add(registrationModel.Key);
                }

                output.Add(TypeOf(serviceModel.ImplementationType));

                method.AddIndentedStatement(
                    services.Invoke(
                        stringBuilder.ToString(),
                        output.ToArray()
                    ));
            }
        }
        
        return method.Name;
    }
}