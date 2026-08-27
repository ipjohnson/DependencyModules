namespace DependencyModules.SourceGenerator.Impl.Models;

/// <summary>
/// The generator's mirror of <c>DependencyModules.Runtime.Attributes.InterceptedMembers</c>.
/// </summary>
/// <remarks>
/// Mirrored rather than referenced. The generator targets netstandard2.0 and does not reference the
/// runtime package — every other attribute it reads is described the same way, as a name and a
/// namespace rather than as a type.
/// </remarks>
[Flags]
public enum InterceptedMemberKinds {
    None = 0,
    Methods = 1,
    Properties = 2,
    Indexers = 4,
    Events = 8,
    All = Methods | Properties | Indexers | Events
}
