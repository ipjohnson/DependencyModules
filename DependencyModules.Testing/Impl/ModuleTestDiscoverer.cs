using Xunit.Sdk;
using Xunit.v3;

namespace DependencyModules.Testing.Impl;

public class ModuleTestDiscoverer : IXunitTestCaseDiscoverer {

    public ValueTask<IReadOnlyCollection<IXunitTestCase>> Discover(
        ITestFrameworkDiscoveryOptions discoveryOptions, IXunitTestMethod testMethod, IFactAttribute factAttribute) {
        
        return new ValueTask<IReadOnlyCollection<IXunitTestCase>>(
            new [] {
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