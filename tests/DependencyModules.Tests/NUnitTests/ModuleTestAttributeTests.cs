using DependencyModules.NUnit.Attributes;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
using Xunit;

namespace DependencyModules.Tests.NUnitTests;

/// <summary>
/// Drives the NUnit integration's test-case building directly, from xUnit.
/// </summary>
/// <remarks>
/// A row that supplies more arguments than the method takes is reported as a non-runnable test,
/// which NUnit counts as a failing one — so a fixture covering it would turn the integration suite
/// red to prove it works. Calling <c>BuildFrom</c> asserts the same behaviour without that.
///
/// It also pins what is built and when: one case per row, placeholders rather than resolved
/// services, and the row kept aside so execution knows which leading arguments are real. Building a
/// container here instead would construct every mock in an assembly during discovery.
/// </remarks>
public class ModuleTestAttributeTests {

    private interface IService;

    /// <summary>
    /// The methods under test, carrying real attributes — the same reflection <c>BuildFrom</c> reads
    /// from at discovery.
    /// </summary>
    private class Samples {

        public void NoParameters() { }

        public void OneServiceParameter(IService service) { }

        public void NumberThenService(int number, IService service) { }

        [ModuleTestCase(1)]
        [ModuleTestCase(2)]
        public void TwoRows(int number, IService service) { }

        [ModuleTestCase(7)]
        public void OneRowCoveringOneOfTwoParameters(int number, IService service) { }

        [ModuleTestCase(1, "text")]
        [ModuleTestCase(2, null)]
        public void RowsNeedingQuoting(int number, string? text) { }

        [ModuleTestCase(1, TestName = "the first one")]
        public void NamedRow(int number) { }

        [ModuleTestCase(1, 2, 3)]
        public void TooManyArguments(int first, int second) { }

        [ModuleTestCase(1, 2)]
        [ModuleTestCase(1, 2, 3)]
        public void OneGoodRowAndOneBad(int first, int second) { }
    }

    [Fact]
    public void BuildsOneCaseWhenThereAreNoRows() {
        var testMethod = Assert.Single(Build(nameof(Samples.OneServiceParameter)));

        Assert.Equal(nameof(Samples.OneServiceParameter), testMethod.Name);
        Assert.Equal(RunState.Runnable, testMethod.RunState);
    }

    /// <summary>
    /// The placeholder stands in for a service that does not exist yet. It has to be there, because
    /// NUnit checks the argument count against the method's parameters when the case is built.
    /// </summary>
    [Fact]
    public void APlaceholderIsSuppliedForEveryParameter() {
        var testMethod = Assert.Single(Build(nameof(Samples.NumberThenService)));

        Assert.Equal(2, testMethod.Arguments.Length);
        Assert.All(testMethod.Arguments, Assert.Null);
    }

    [Fact]
    public void AMethodWithNoParametersBuildsWithNoArguments() {
        Assert.Empty(Assert.Single(Build(nameof(Samples.NoParameters))).Arguments);
    }

    [Fact]
    public void BuildsOneCasePerRow() {
        var built = Build(nameof(Samples.TwoRows));

        Assert.Equal(2, built.Length);
        Assert.Equal(1, built[0].Arguments[0]);
        Assert.Equal(2, built[1].Arguments[0]);
    }

    /// <summary>
    /// A row covers the leading parameters only; the rest stay null until the container fills them.
    /// </summary>
    [Fact]
    public void ARowLeavesTheRemainingParametersToTheContainer() {
        var testMethod = Assert.Single(Build(nameof(Samples.OneRowCoveringOneOfTwoParameters)));

        Assert.Equal(7, testMethod.Arguments[0]);
        Assert.Null(testMethod.Arguments[1]);
    }

    [Fact]
    public void RowsAreNamedAfterTheirOwnArguments() {
        Assert.Equal("OneRowCoveringOneOfTwoParameters(7)",
            Assert.Single(Build(nameof(Samples.OneRowCoveringOneOfTwoParameters))).Name);
    }

    /// <summary>
    /// Only the row's arguments appear. Naming a case after the trailing placeholders would produce
    /// "OneRowCoveringOneOfTwoParameters(7, null)", where the null is a service that will exist by
    /// the time the test runs.
    /// </summary>
    [Fact]
    public void ARowsNameOmitsTheParametersTheContainerSupplies() {
        Assert.DoesNotContain("null",
            Assert.Single(Build(nameof(Samples.OneRowCoveringOneOfTwoParameters))).Name);
    }

    [Fact]
    public void StringsAreQuotedAndNullsSpelledOutInARowsName() {
        var built = Build(nameof(Samples.RowsNeedingQuoting));

        Assert.Equal("RowsNeedingQuoting(1, \"text\")", built[0].Name);
        Assert.Equal("RowsNeedingQuoting(2, null)", built[1].Name);
    }

    [Fact]
    public void ARowCanNameItself() {
        Assert.Equal("the first one", Assert.Single(Build(nameof(Samples.NamedRow))).Name);
    }

    /// <summary>
    /// The case a live fixture cannot cover, because a non-runnable test is a failing one.
    /// </summary>
    [Fact]
    public void ARowWithTooManyArgumentsIsReportedRatherThanThrown() {
        var testMethod = Assert.Single(Build(nameof(Samples.TooManyArguments)));

        Assert.Equal(RunState.NotRunnable, testMethod.RunState);

        var reason = Assert.IsType<string>(testMethod.Properties.Get(PropertyNames.SkipReason));

        Assert.Contains("supplied 3 arguments to a method taking 2", reason);
    }

    /// <summary>
    /// One bad row must not take the rest of the fixture with it, which is what throwing during
    /// discovery would do.
    /// </summary>
    [Fact]
    public void AGoodRowStillBuildsAlongsideABadOne() {
        var built = Build(nameof(Samples.OneGoodRowAndOneBad));

        Assert.Equal(2, built.Length);
        Assert.Equal(RunState.Runnable, built[0].RunState);
        Assert.Equal(RunState.NotRunnable, built[1].RunState);
    }

    private static TestMethod[] Build(string methodName) {
        var method = typeof(Samples).GetMethod(methodName)!;

        return new ModuleTestAttribute()
            .BuildFrom(new MethodWrapper(typeof(Samples), method), suite: null)
            .ToArray();
    }
}
