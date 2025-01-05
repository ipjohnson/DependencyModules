using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl;
using DependencyModules.SourceGenerator.Impl.Models;
using static CSharpAuthor.SyntaxHelpers;

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
                    constructor.AddParameter(constructorParameter.ParameterType, constructorParameter.ParameterName);

                constructor.Assign(parameter).To(field.Name);
            }
        }

        foreach (var propertyInfoModel in model.PropertyInfoModels) {
            if (propertyInfoModel.IsReadOnly) {
                continue;
            }

            var propertyType = propertyInfoModel.PropertyType;

            var property = attributeClass.AddProperty(propertyType, propertyInfoModel.PropertyName);

        }

        SetupProviderMethod(model, attributeClass);
    }

    private void SetupProviderMethod(ModuleEntryPointModel model, ClassDefinition attributeClass) {
        var method = attributeClass.AddMethod("GetModule");

        method.SetReturnType(KnownTypes.DependencyModules.Interfaces.IDependencyModule);

        var newModule =
            method.Assign(
                New(model.EntryPointType,
                    attributeClass.Fields.Select(f => f.Instance).OfType<object>().ToArray())).ToVar("newModule");

        foreach (var propertyInfoModel in model.PropertyInfoModels) {
            BaseBlockDefinition block = method;

            if (propertyInfoModel.PropertyType.IsNullable) {
                block =
                    method.If(NotEquals(propertyInfoModel.PropertyName, Null()));

            }

            block.Assign(propertyInfoModel.PropertyName).To(newModule.Property(propertyInfoModel.PropertyName));
        }

        method.Return(newModule);
    }
}