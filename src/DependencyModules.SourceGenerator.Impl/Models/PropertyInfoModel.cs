using CSharpAuthor;

namespace DependencyModules.SourceGenerator.Impl.Models;

/// <summary>
/// A property declared on a module, and whether it is one of that module's parameters.
/// </summary>
/// <param name="IsReadOnly">No set accessor. Nothing to configure, so not a parameter.</param>
/// <param name="IsStatic">Belongs to the type rather than the instance, so not a parameter.</param>
/// <param name="IsVisibleToAttribute">
/// Reachable from another type in the same assembly, which is where the generated attribute sits.
/// <c>public</c>, <c>internal</c> and <c>protected internal</c> are; <c>private</c>,
/// <c>protected</c>, <c>private protected</c> and an unmodified declaration are not.
/// </param>
public record PropertyInfoModel(
    ITypeDefinition PropertyType,
    string PropertyName,
    bool IsReadOnly,
    bool IsStatic,
    bool IsVisibleToAttribute) {

    /// <summary>
    /// Whether this property is carried across to the generated attribute as a module parameter.
    /// </summary>
    /// <remarks>
    /// One predicate rather than the same three-part condition written at each site. It was written
    /// three times — the DM0018 report, the attribute's property list and the attribute's copy-back
    /// — and the accessibility term was missing from all three, which is how a private property came
    /// to be emitted onto a public attribute as <c>CS0122</c> in generated code.
    /// </remarks>
    public bool IsModuleParameter => !IsReadOnly && !IsStatic && IsVisibleToAttribute;
}
