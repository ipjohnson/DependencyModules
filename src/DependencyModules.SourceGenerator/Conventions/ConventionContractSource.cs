namespace DependencyModules.Conventions;

/// <summary>
/// The names the generator matches convention declarations against.
/// </summary>
/// <remarks>
/// <para>
/// The types themselves are declared in <c>DependencyModules.Runtime</c>, under this same
/// namespace. They used to be emitted into every consuming compilation, which forced them to be
/// <c>internal</c> — and therefore forced explicit interface implementation — and made CS0436
/// unavoidable between two assemblies that both emitted them and referenced each other.
/// </para>
/// <para>
/// An analyzer must not load the runtime assembly, so the names are duplicated here as strings
/// rather than read off the types. <c>ConventionContractTests</c> asserts the two agree; without it
/// a rename on either side would stop every convention matching, silently.
/// </para>
/// </remarks>
public static class ConventionContractSource {

    /// <summary>
    /// The namespace the contracts are declared in, and the metadata prefix the generator matches
    /// declarations against. Deliberately not this assembly's own namespace: the contracts ship in
    /// DependencyModules.Runtime, and sharing a namespace across the two would be ambiguous wherever
    /// both are referenced.
    /// </summary>
    public const string Namespace = "DependencyModules.Runtime.Conventions";

    /// <summary>
    /// The interface a module implements to opt into convention registration.
    /// </summary>
    public const string ConventionModule = "IConventionModule";

    /// <summary>
    /// The method the generator reads. Implemented explicitly, so the name is fixed.
    /// </summary>
    public const string ConventionMethod = "Conventions";

}
