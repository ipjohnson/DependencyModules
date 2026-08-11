using DependencyModules.NUnit.Attributes;
using DependencyModules.Testing.Attributes;
using NUnit.Framework;

namespace SutProject.NUnitTests;

/// <summary>
/// Data rows, which NUnit's own <c>[TestCase]</c> cannot supply for a module test.
/// </summary>
/// <remarks>
/// <c>[TestCase]</c> requires a row to fill every parameter, and checks that at build time — so it
/// cannot express a row that covers the first parameters while the container covers the rest, which
/// is what a module test with data is. <c>[ModuleTestCase]</c> is the same idea without that rule.
/// </remarks>
public class DataRowTests {

    [ModuleTest(typeof(SutModule))]
    [ModuleTestCase(1)]
    [ModuleTestCase(2)]
    [ModuleTestCase(3)]
    public void RowSuppliesTheLeadingParameterAndTheContainerTheRest(
        int number, ISingletonService singletonService) {
        Assert.That(number, Is.InRange(1, 3));
        Assert.That(singletonService, Is.Not.Null, "resolved from the container, not from the row");

        SeenNumbers.Add(number);
    }

    public static readonly List<int> SeenNumbers = [];

    [ModuleTest(typeof(SutModule))]
    [ModuleTestCase("first", 1)]
    [ModuleTestCase("second", 2)]
    public void SeveralLeadingParametersComeFromTheRow(
        string word, int number, ISingletonService singletonService) {
        Assert.That(word, Is.AnyOf("first", "second"));
        Assert.That(number, Is.AnyOf(1, 2));
        Assert.That(singletonService, Is.Not.Null);
    }

    /// <summary>A row can fill every parameter, leaving nothing for the container.</summary>
    [ModuleTest(typeof(SutModule))]
    [ModuleTestCase(4, 5)]
    public void ARowMayCoverEveryParameter(int first, int second) {
        Assert.That(first + second, Is.EqualTo(9));
    }

    /// <summary>Rows compose with the parameter attributes, which know nothing about rows.</summary>
    [ModuleTest(typeof(SutModule))]
    [ModuleTestCase(10)]
    [ModuleTestCase(20)]
    public void RowsComposeWithInjectedValues(
        int number, [InjectValues("supplied")] NeedsAValue needsAValue) {
        Assert.That(number, Is.AnyOf(10, 20));
        Assert.That(needsAValue.Text, Is.EqualTo("supplied"));
        Assert.That(needsAValue.SingletonService, Is.Not.Null);
    }

    [ModuleTest(typeof(SutModule))]
    [ModuleTestCase(1, TestName = "a row can name itself")]
    public void NamedRow(int number) {
        Assert.That(number, Is.EqualTo(1));
    }

    public class NeedsAValue(ISingletonService singletonService, string text) {
        public ISingletonService SingletonService { get; } = singletonService;

        public string Text { get; } = text;
    }
}

/// <summary>
/// Each row is its own test case, so each gets its own container — the same rule repetitions follow.
/// </summary>
public class DDataRowReport {

    [Test]
    public void EveryRowRanExactlyOnce() {
        Assert.That(DataRowTests.SeenNumbers, Is.EquivalentTo(new[] { 1, 2, 3 }));
    }
}
