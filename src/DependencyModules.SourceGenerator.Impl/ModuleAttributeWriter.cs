using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl;
using DependencyModules.SourceGenerator.Impl.Models;
using DependencyModules.SourceGenerator.Impl.Utilities;
using static CSharpAuthor.SyntaxHelpers;

namespace DependencyModules.SourceGenerator.Impl;

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
            
            // Guarded whatever the declared nullability. An attribute property is null until
            // somebody assigns it, and `?` is an annotation rather than a runtime fact — so gating
            // the guard on it meant `public string Label { get; set; } = "default";` had its
            // initialiser overwritten with null by a composition that never mentioned Label, while
            // the same property written `string?` survived.
            //
            // A value-typed property compares as always-true here and is assigned unconditionally,
            // which is the existing behaviour: `int` is 0 until assigned and 0 is a legitimate
            // value, so null cannot express "unset" for one either way. CS0472 says as much, and
            // BaseAttributeWriter already wraps the class in a pragma for it.
            var block = method.If(NotEquals(propertyInfoModel.PropertyName, Null()));

            block.Assign(propertyInfoModel.PropertyName).To(newModule.Property(propertyInfoModel.PropertyName));
        }

        method.Return(newModule);
    }
}