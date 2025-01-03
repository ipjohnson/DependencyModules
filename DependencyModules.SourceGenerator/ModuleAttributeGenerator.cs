using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;
using DependencyModules.SourceGenerator.Impl;
using DependencyModules.SourceGenerator.Impl.Models;

namespace DependencyModules.SourceGenerator;

public class ModuleAttributeGenerator {

    public void Generate(ModuleEntryPointModel model, ClassDefinition classDefinition) {
        var attributeClass = classDefinition.AddClass("ModuleAttribute");
        
        attributeClass.AddBaseType(TypeDefinition.Get(typeof(Attribute)));
        attributeClass.AddBaseType(KnownTypes.DependencyModules.Interfaces.IDependencyModuleProvider);

        if (model.Parameters.Count > 0) {
            var constructor = attributeClass.AddConstructor();
        
            foreach (var constructorParameter in model.Parameters) {
                var field = attributeClass.AddField(
                    constructorParameter.ParameterType, constructorParameter.ParameterName + "Field");

                var parameter = 
                    constructor.AddParameter(constructorParameter.ParameterType, constructorParameter.ParameterName );
            
                constructor.Assign(parameter).To(field.Name);
            }
        }
        
        SetupProviderMethod(model, attributeClass);
    }

    private void SetupProviderMethod(ModuleEntryPointModel model, ClassDefinition attributeClass) {
        var method = attributeClass.AddMethod("GetModule");

        method.SetReturnType(KnownTypes.DependencyModules.Interfaces.IDependencyModule);
        
        method.Return(New(model.EntryPointType, 
            attributeClass.Fields.Select(f => f.Instance).OfType<object>().ToArray()));
    }
}