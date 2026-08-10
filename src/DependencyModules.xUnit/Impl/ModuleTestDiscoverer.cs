using DependencyModules.xUnit.Attributes;
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
        //
        // label is named to pick an overload, not because a value is wanted. xunit.v3 3.x added a
        // second GetTestCaseDetails taking a trailing label, and since every added parameter on
        // both is optional, a three-argument call matches the two equally well and is ambiguous.
        // Naming a parameter only the newer one declares resolves it. A module test has no label,
        // which is what null says.
        var details = TestIntrospectionHelper.GetTestCaseDetails(
            discoveryOptions, testMethod, factAttribute, label: null);

        return new ValueTask<IReadOnlyCollection<IXunitTestCase>>(
            new[] {
                new ModuleTestCase(
                    testMethod: details.ResolvedTestMethod,
                    testCaseDisplayName: details.TestCaseDisplayName,
                    uniqueID: details.UniqueID,
                    @explicit: details.Explicit,
                    // New in 3.x, and forwarded rather than defaulted: introspection already reads
                    // [Fact(SkipExceptions = …)] off the attribute, so dropping it here would leave
                    // [ModuleTest] silently ignoring a skip condition that [Fact] honours.
                    skipExceptions: details.SkipExceptions,
                    skipReason: details.SkipReason,
                    skipType: details.SkipType,
                    skipUnless: details.SkipUnless,
                    skipWhen: details.SkipWhen,
                    traits: testMethod.Traits.ToWritableTraits(StringComparer.OrdinalIgnoreCase),
                    // Introspection reads these off the attribute, which captures them from the
                    // usage site. Not forwarding them left every module test without a source
                    // location, so a test explorer had nowhere to navigate to.
                    sourceFilePath: details.SourceFilePath,
                    sourceLineNumber: details.SourceLineNumber,
                    timeout: details.Timeout
                )
            }
        );
    }
}