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

        return new ValueTask<IReadOnlyCollection<IXunitTestCase>>(
            new[] {
                new ModuleTestCase(
                    testMethod,
                    testMethod.MethodName,
                    testMethod.MethodName,
                    false
                )
            }
        );
    }
}