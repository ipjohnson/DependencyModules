using DependencyModules.NUnit.Attributes;
using NUnit.Framework;

namespace DependencyModules.Tests.NUnitTests;

/// <summary>
/// <c>[ModuleTest]</c> methods that also carry a data attribute of NUnit.
/// </summary>
/// <remarks>
/// In a file of their own, because <c>NUnit.Framework</c> and <c>Xunit</c> both declare
/// <c>[Theory]</c> and <c>TestResult</c>. The xUnit project has no NUnit adapter, so nothing runs
/// these as tests.
/// </remarks>
public class NUnitDataSamples
{
    public static IEnumerable<object[]> Sevens() =>
        [
            [7],
        ];

    [ModuleTest]
    [ModuleTestCase(1)]
    [TestCase(7)]
    public void WithTestCase(int number) { }

    [ModuleTest]
    [ModuleTestCase(1)]
    [TestCaseSource(nameof(Sevens))]
    public void WithTestCaseSource(int number) { }

    [ModuleTest]
    [ModuleTestCase(1)]
    public void WithValues([Values(7)] int number) { }

    [ModuleTest]
    [ModuleTestCase(1)]
    public void WithRange([Range(7, 8)] int number) { }

    [ModuleTest]
    [TestCase(7)]
    public void OnlyTestCase(int number) { }
}
