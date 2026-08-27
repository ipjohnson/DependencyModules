namespace DependencyModules.Runtime.Attributes;

/// <summary>
/// Which kinds of member an interception is placed around.
/// </summary>
/// <remarks>
/// <para>
/// Interception covers the whole interface by default, which is the right default — an interceptor
/// written for auditing or retry has no way to know which members matter, and leaving one out
/// silently is the failure this library works hardest to avoid.
/// </para>
/// <para>
/// It is the wrong default for a service whose interface carries properties. A timing or logging
/// interceptor placed around a property getter records a call per read, which is noise rather than
/// signal, and the workaround was to key metrics by member name and filter afterwards. Naming the
/// kinds to cover says it at the declaration instead.
/// </para>
/// <para>
/// A member left out is still forwarded — the wrapper implements the whole interface either way.
/// It just does not run through the interceptor chain.
/// </para>
/// </remarks>
[Flags]
public enum InterceptedMembers {
    /// <summary>Ordinary methods.</summary>
    Methods = 1,

    /// <summary>Property getters and setters.</summary>
    Properties = 2,

    /// <summary>Indexer getters and setters.</summary>
    Indexers = 4,

    /// <summary>Event add and remove accessors.</summary>
    Events = 8,

    /// <summary>Everything the interface declares. The default.</summary>
    All = Methods | Properties | Indexers | Events
}
