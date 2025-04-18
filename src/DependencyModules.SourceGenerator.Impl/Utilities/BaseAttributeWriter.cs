using System.Text;
using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

public abstract class BaseAttributeWriter<T> where T : IClassModel {
    public void CreateAttributeClass(IConstructContainer container, T model) {
        var attributeClass = ConstructClassDefinition(container, model);

        AddClassTraits(attributeClass);

        CreateConstructor(container, attributeClass, model);

        CreateProperties(container, attributeClass, model);
        
        CustomImplementation(container, attributeClass, model);
    }

    private static void AddClassTraits(ClassDefinition attributeClass) {
        attributeClass.EnableNullable();
        attributeClass.WrapInPragma("CS0472");
        attributeClass.AddLeadingTrait(
            new UsageAttributeComponent(
                "[AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly | AttributeTargets.Method | AttributeTargets.Parameter, AllowMultiple = true)]"));
    }

    protected virtual void CustomImplementation(IConstructContainer container, ClassDefinition attributeClass, T model) {
        
    }

    private static ClassDefinition ConstructClassDefinition(IConstructContainer container, T model) {
        var attributeClass = container.AddClass( model.ClassType.Name + "Attribute");
        
        attributeClass.Modifiers |= ComponentModifier.Public | ComponentModifier.Partial;
        attributeClass.AddBaseType(TypeDefinition.Get("System","Attribute"));
        attributeClass.AddBaseType(KnownTypes.DependencyModules.Interfaces.IDependencyModuleProvider);
        
        return attributeClass;
    }

    protected virtual void CreateProperties(IConstructContainer container, ClassDefinition attributeClass, T model) {
        foreach (var propertyInfoModel in model.PropertyInfoModels) {
            if (propertyInfoModel.IsReadOnly || propertyInfoModel.IsStatic) {
                continue;
            }

            var propertyType = propertyInfoModel.PropertyType;

            if (propertyType.IsNullable) {
                propertyType = TypeDefinition.Get(propertyType.TypeDefinitionEnum, propertyType.Namespace, propertyType.Name, propertyType.IsArray);
            }

            var property = attributeClass.AddProperty(propertyType, propertyInfoModel.PropertyName);

            var stringBuilder = new StringBuilder();
            propertyType.WriteTypeName(stringBuilder, TypeOutputMode.Global);
            
            property.DefaultValue = 
                new WrapStatement(CodeOutputComponent.Get(stringBuilder.ToString()), "default(", ")!");
        }
    }

    protected virtual void CreateConstructor(IConstructContainer container, ClassDefinition attributeClass, T model) {
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
    }
}