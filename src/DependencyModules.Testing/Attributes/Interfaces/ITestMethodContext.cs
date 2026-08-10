using System.Reflection;

namespace DependencyModules.Testing.Attributes.Interfaces;

/// <summary>
/// The test method a container is being built for.
/// </summary>
/// <remarks>
/// Every test framework has its own model of a test method — xUnit v3 has <c>IXunitTestMethod</c>,
/// carrying identity that survives serialization, merged traits, and generic resolution alongside the
/// reflection. None of that is needed to decide what to register in a test's container, and taking a
/// dependency on it would bind every mocking package to a single test framework. This carries the part
/// that is common; a framework integration supplies its own implementation over its own model.
/// </remarks>
public interface ITestMethodContext {

    /// <summary>
    /// The method under test.
    /// </summary>
    /// <remarks>
    /// A real <see cref="MethodInfo"/> rather than an abstraction over one. The extension methods in
    /// <c>AttributeUtility</c> hang off this, and it is the same instance the framework integration
    /// reads parameters from, so a hook sees exactly the signature the test will be invoked with.
    /// </remarks>
    MethodInfo Method {
        get;
    }

    /// <summary>
    /// Every attribute in scope for the method, widest scope first: assembly, then declaring type,
    /// then the method itself.
    /// </summary>
    /// <remarks>
    /// The integration has already walked and ordered these, so an implementation looking for one can
    /// read the list rather than paying for the walk again.
    ///
    /// Note the ordering is the reverse of <c>AttributeUtility.GetTestAttribute</c>, which answers
    /// "the most specific one wins" and so looks at the method first. This list is in the order things
    /// are applied, where the most specific runs last and therefore wins.
    /// </remarks>
    IReadOnlyList<Attribute> Attributes {
        get;
    }
}
