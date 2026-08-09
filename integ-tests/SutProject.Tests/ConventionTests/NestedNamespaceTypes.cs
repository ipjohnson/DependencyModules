using SutProject.Tests.ConventionTests;

namespace SutProject.Tests.ConventionTests.Nested;

/// <summary>
/// Lives one namespace below the conventions, so a prefix filter reaches it and an exact one does
/// not. That difference is the whole point of InExactNamespaces.
/// </summary>
public class NestedScanned : INamespaceScanned {
    /// <inheritdoc />
    public string Name => "nested";
}
