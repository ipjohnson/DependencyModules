using DependencyModules.xUnit.Attributes;
using Xunit.Internal;
using Xunit.Sdk;
using Xunit.v3;

namespace DependencyModules.xUnit.Impl;

/// <summary>
/// A custom test case discoverer for identifying and creating test cases marked with <see cref="ModuleTestAttribute"/>.
/// </summary>
/// <remarks>
/// This class is responsible for discovering test methods annotated with the <see cref="ModuleTestAttribute"/>
/// and creating corresponding <see cref="IXunitTestCase"/> instances. It integrates with the xUnit framework
/// by implementing the <see cref="IXunitTestCaseDiscoverer"/> interface.
/// </remarks>
/// <seealso cref="ModuleTestAttribute"/>
/// <seealso cref="IXunitTestCaseDiscoverer"/>
public class ModuleTestDiscoverer : IXunitTestCaseDiscoverer {

    /// <summary>
    /// Discovers test cases for the provided method using the xUnit framework
    /// and returns a collection of test cases to be executed.
    /// </summary>
    /// <param name="discoveryOptions">
    /// The options controlling the discovery process, such as filters or settings.
    /// </param>
    /// <param name="testMethod">
    /// The method for which test cases will be generated.
    /// </param>
    /// <param name="factAttribute">
    /// The fact attribute decorating the test method, used to filter or process the test case.
    /// </param>
    /// <returns>
    /// A task that, when completed, contains a read-only collection of discovered test cases specific to the provided method.
    /// </returns>
    public ValueTask<IReadOnlyCollection<IXunitTestCase>> Discover(
        ITestFrameworkDiscoveryOptions discoveryOptions, IXunitTestMethod testMethod, IFactAttribute factAttribute) {

        // Delegate to xUnit's own introspection rather than deriving these by hand. The bare method
        // name is not unique across test classes, and xUnit silently drops a test case whose ID
        // collides with one already discovered. This also picks up display name formatting, the
        // skip and explicit attributes, and the timeout, consistently with [Fact] and [Theory].
        var details = TestIntrospectionHelper.GetTestCaseDetails(discoveryOptions, testMethod, factAttribute);

        return new ValueTask<IReadOnlyCollection<IXunitTestCase>>(
            new[] {
                new ModuleTestCase(
                    details.ResolvedTestMethod,
                    details.TestCaseDisplayName,
                    details.UniqueID,
                    details.Explicit,
                    details.SkipReason,
                    details.SkipType,
                    details.SkipUnless,
                    details.SkipWhen,
                    testMethod.Traits.ToReadWrite(StringComparer.OrdinalIgnoreCase),
                    timeout: details.Timeout
                )
            }
        );
    }
}