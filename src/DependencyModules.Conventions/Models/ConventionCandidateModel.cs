using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;

namespace DependencyModules.Conventions.Models;

/// <summary>
/// An interface a candidate implements, in the construction it actually implements it in.
/// </summary>
/// <param name="InterfaceType">
/// The closed form, which is what gets registered. A candidate matched by
/// <c>RegisterAll(typeof(IHandler&lt;,&gt;))</c> registers against
/// <c>IHandler&lt;CreateOrder, OrderId&gt;</c>, not against the open definition.
/// </param>
/// <param name="DefinitionKey">Namespace, name and arity, for matching an open convention.</param>
/// <param name="ViaTypeName">
/// Null when the interface is written on the declaration. Otherwise the type it was reached
/// through — the written interface that extends it, or the base class that implements it.
///
/// This is what DM0010 reports, and it is the whole reason matching through inheritance does not
/// read as luck: the class says why it is in the container, at the class.
/// </param>
public record ImplementedInterfaceModel(
    ITypeDefinition InterfaceType,
    string DefinitionKey,
    string? ViaTypeName);

/// <summary>
/// A class or record a convention could match.
/// </summary>
/// <remarks>
/// The two interface lists are kept apart rather than flattened because the reach of a convention
/// is a declared choice. Interfaces reached by declaration — written on the class, or extended by
/// something written on the class — always match: an interface saying it extends another is a
/// deliberate statement that it is substitutable for it. Interfaces reached only through a base
/// class match only when the convention calls <c>IncludeBaseClasses()</c>, because extending a
/// class is a statement about implementation reuse, and every subclass added later would otherwise
/// join the convention with nobody revisiting it.
/// </remarks>
public record ConventionCandidateModel(
    ITypeDefinition ImplementationType,
    IReadOnlyList<ImplementedInterfaceModel> DeclaredInterfaces,
    IReadOnlyList<ImplementedInterfaceModel> BaseClassInterfaces,
    ConstructorInfoModel? Constructor,
    bool HasAccessibleConstructor,
    LocationModel Location) {

    public static readonly ConventionCandidateModel Ignore = new(
        TypeDefinition.Get("", "Ignore"),
        Array.Empty<ImplementedInterfaceModel>(),
        Array.Empty<ImplementedInterfaceModel>(),
        null,
        true,
        LocationModel.None);

    public bool IsIgnored => ReferenceEquals(this, Ignore) || ImplementationType.Equals(Ignore.ImplementationType);

    /// <summary>
    /// The interfaces visible to a convention with the given reach.
    /// </summary>
    public IEnumerable<ImplementedInterfaceModel> InterfacesInReach(bool includeBaseClasses) =>
        includeBaseClasses ? DeclaredInterfaces.Concat(BaseClassInterfaces) : DeclaredInterfaces;

    public virtual bool Equals(ConventionCandidateModel? other) =>
        other is not null &&
        ImplementationType.Equals(other.ImplementationType) &&
        HasAccessibleConstructor == other.HasAccessibleConstructor &&
        Location == other.Location &&
        ModelEquality.ListEquals(DeclaredInterfaces, other.DeclaredInterfaces) &&
        ModelEquality.ListEquals(BaseClassInterfaces, other.BaseClassInterfaces) &&
        CompareConstructor(Constructor, other.Constructor);

    private static bool CompareConstructor(ConstructorInfoModel? x, ConstructorInfoModel? y) {
        if (x is null && y is null) return true;
        if (x is null || y is null) return false;

        return ModelEquality.ListEquals(x.Parameters, y.Parameters);
    }

    public override int GetHashCode() {
        unchecked {
            var hash = ImplementationType.GetHashCode();
            hash = hash * 31 + HasAccessibleConstructor.GetHashCode();
            hash = hash * 31 + ModelEquality.ListHashCode(DeclaredInterfaces);
            hash = hash * 31 + ModelEquality.ListHashCode(BaseClassInterfaces);
            return hash;
        }
    }
}
