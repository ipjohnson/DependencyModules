using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl;
using DependencyModules.SourceGenerator.Impl.Models;
using static CSharpAuthor.SyntaxHelpers;

namespace DependencyModules.SourceGenerator;

public class UsageAttributeComponent : BaseOutputComponent
{
    private readonly string _usage;

    public UsageAttributeComponent(string usage) {
        _usage = usage;
    }

    protected override void WriteComponentOutput(IOutputContext outputContext)
    {
        outputContext.WriteIndent();
        outputContext.WriteLine(_usage);
    }
}

public class ModuleAttributeGenerator {
    public void Generate(ModuleEntryPointModel model, ClassDefinition classDefinition) {
        var attributeClass = classDefinition.AddClass("Attribute");
        
        attributeClass.Modifiers |= ComponentModifier.Public | ComponentModifier.Partial;
        attributeClass.AddBaseType(TypeDefinition.Get("BaseAttribute = System.Attribute","BaseAttribute"));
        attributeClass.AddBaseType(KnownTypes.DependencyModules.Interfaces.IDependencyModuleProvider);
        attributeClass.AddLeadingTrait(
            new UsageAttributeComponent(
                "[AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly | AttributeTargets.Method | AttributeTargets.Parameter, AllowMultiple = true)]"));

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
            if (propertyInfoModel.IsReadOnly || propertyInfoModel.IsStatic) {
                continue;
            }

            var propertyType = propertyInfoModel.PropertyType;

            if (propertyType.IsNullable) {
                propertyType = TypeDefinition.Get(propertyType.TypeDefinitionEnum, propertyType.Namespace, propertyType.Name);
            }

            var property = attributeClass.AddProperty(propertyType, propertyInfoModel.PropertyName);

            property.DefaultValue = 
                new WrapStatement(CodeOutputComponent.Get(propertyType.Name), "default(", ")!");
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
            if (propertyInfoModel.IsReadOnly || propertyInfoModel.IsStatic) {
                continue;
            }
            
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