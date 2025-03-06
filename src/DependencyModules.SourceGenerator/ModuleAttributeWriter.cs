using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl;
using DependencyModules.SourceGenerator.Impl.Models;
using DependencyModules.SourceGenerator.Impl.Utilities;
using static CSharpAuthor.SyntaxHelpers;

namespace DependencyModules.SourceGenerator;

public class ModuleAttributeWriter : BaseAttributeWriter<ModuleEntryPointModel> {

    protected override void CustomImplementation(IConstructContainer container, ClassDefinition attributeClass, ModuleEntryPointModel model) {
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