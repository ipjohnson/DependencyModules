using System.Reflection;
using DependencyModules.xUnit.Attributes;
using DependencyModules.xUnit.Impl;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

namespace DependencyModules.Tests.xUnitTests;

/// <summary>
/// Drives <see cref="ModuleTestCase.CreateTests"/> directly, for the data-driven shapes.
///
/// The failure these cover is the one a test integration can least afford: four of the five row
/// sources below expanded to <em>zero</em> cases and the run reported <c>Passed!</c>. Moving a row
/// set from [InlineData] to [MemberData] therefore dropped the coverage silently. Asserting on the
/// count of tests produced is the only way that becomes visible, because every integration test
/// runs through [ModuleTest] and a case that is never created is a suite that is quietly smaller.
/// </summary>
public class ModuleTestCaseDataTests {

    /// <summary>
    /// Regression test. [MemberData] resolves its member off <c>ITypeAwareDataAttribute.MemberType</c>,
    /// which xUnit back-fills in <c>ExtensibilityPointFactory.GetMethodDataAttributes</c> — a path
    /// this integration does not use, because it also sweeps assembly- and class-level attributes.
    /// Left null, <c>MemberDataAttributeBase.GetData</c> returns an empty collection rather than
    /// throwing, so the rows vanished without a diagnostic.
    /// </summary>
    [Fact]
    public async Task MemberData_WithoutExplicitMemberType_ProducesOneTestPerRow() {
        var tests = await CreateTests(nameof(DataSample.FromTheoryData));

        Assert.Equal(2, tests.Count);
    }

    [Fact]
    public async Task MemberData_ReturningObjectArrays_ProducesOneTestPerRow() {
        var tests = await CreateTests(nameof(DataSample.FromObjectArrays));

        Assert.Equal(2, tests.Count);
    }

    [Fact]
    public async Task MemberData_ReturningTheoryDataRows_ProducesOneTestPerRow() {
        var tests = await CreateTests(nameof(DataSample.FromTheoryDataRows));

        Assert.Equal(2, tests.Count);
    }

    /// <summary>
    /// The control that made the bug invisible: an explicit MemberType needs no back-fill, so this
    /// shape worked throughout.
    /// </summary>
    [Fact]
    public async Task MemberData_WithExplicitMemberType_ProducesOneTestPerRow() {
        var tests = await CreateTests(nameof(DataSample.FromExplicitMemberType));

        Assert.Equal(2, tests.Count);
    }

    [Fact]
    public async Task ClassData_ProducesOneTestPerRow() {
        var tests = await CreateTests(nameof(DataSample.FromClassData));

        Assert.Equal(2, tests.Count);
    }

    /// <summary>
    /// The other control. [InlineData] carries its own literals and never needed the back-fill.
    /// </summary>
    [Fact]
    public async Task InlineData_ProducesOneTestPerRow() {
        var tests = await CreateTests(nameof(DataSample.FromInlineData));

        Assert.Equal(2, tests.Count);
    }

    /// <summary>
    /// The shape the guide actually teaches, and the one the field report ran into: the row supplies
    /// the leading parameters and the container supplies the trailing one. Kept as its own set of
    /// cases because the row-count and the argument-resolution halves fail independently — the
    /// original report attributed the whole thing to the trailing parameter, which is not where it
    /// was.
    /// </summary>
    [Theory]
    [InlineData(nameof(DataSample.TheoryDataWithContainerParameter))]
    [InlineData(nameof(DataSample.ObjectArraysWithContainerParameter))]
    [InlineData(nameof(DataSample.TheoryDataRowsWithContainerParameter))]
    [InlineData(nameof(DataSample.ClassDataWithContainerParameter))]
    [InlineData(nameof(DataSample.InlineDataWithContainerParameter))]
    public async Task RowSupplyingFewerArgumentsThanTheMethodTakes_ProducesOneTestPerRow(string methodName) {
        var tests = await CreateTests(methodName);

        Assert.Equal(2, tests.Count);
    }

    /// <summary>
    /// A row source that legitimately yields nothing must fail rather than pass, which is xUnit's
    /// own default for a theory without data. Returning zero tests silently is what turned the bug
    /// above into a green suite instead of a red one.
    /// </summary>
    [Fact]
    public async Task DataAttributeYieldingNoRows_Fails() {
        var exception = await Assert.ThrowsAnyAsync<Exception>(
            async () => await CreateTests(nameof(DataSample.FromEmptySource)));

        Assert.Contains(nameof(DataSample.FromEmptySource), exception.Message);
    }

    /// <summary>
    /// A method with no data attribute at all is the ordinary case and still produces exactly one
    /// test — the guard above must not catch it.
    /// </summary>
    [Fact]
    public async Task NoDataAttribute_ProducesOneTest() {
        var tests = await CreateTests(nameof(DataSample.NoRows));

        Assert.Single(tests);
    }

    private static async Task<IReadOnlyCollection<IXunitTest>> CreateTests(string methodName) {
        var testMethod = BuildTestMethod(typeof(DataSample), methodName);

        var testCases = await new ModuleTestDiscoverer().Discover(
            new DiscoveryOptions(), testMethod, new ModuleTestAttribute());

        var testCase = Assert.Single(testCases);

        return await testCase.CreateTests();
    }

    private static IXunitTestMethod BuildTestMethod(Type testClass, string methodName) {
        var assembly = new XunitTestAssembly(testClass.Assembly);
        var collection = new XunitTestCollection(assembly, null, false, "Test collection");
        var xunitClass = new XunitTestClass(testClass, collection);
        var method = testClass.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)!;

        return new XunitTestMethod(xunitClass, method, []);
    }

    private class DiscoveryOptions : ITestFrameworkDiscoveryOptions {
        private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);

        public TValue? GetValue<TValue>(string name) =>
            _values.TryGetValue(name, out var value) && value is TValue typed ? typed : default;

        public void SetValue<TValue>(string name, TValue value) => _values[name] = value;

        public string ToJson() => "{}";
    }

    /// <summary>
    /// Nested and un-attributed on purpose: annotating it with [ModuleTest] would make the xUnit
    /// analyzer treat it as a test class. The discoverer is handed the attribute directly instead.
    /// </summary>
    // xUnit1008 wants a [Theory] beside every data attribute. These methods are deliberately not
    // test methods — they are reflection fixtures the discoverer is pointed at by hand — and adding
    // [Theory] would make the outer suite run them.
    //
    // xUnit1037 fires on every trailing-container-parameter method below, because to xUnit a row
    // supplying fewer arguments than the method takes is a mistake. Under [ModuleTest] it is the
    // documented feature — the container supplies the rest — so the rule cannot hold here. Worth
    // noting that the guide teaches this shape without mentioning that it trips an analyzer error.
#pragma warning disable xUnit1008, xUnit1037
    private class DataSample {
        public static TheoryData<string> Rows => new("first", "second");

        public static IEnumerable<object[]> ObjectArrayRows => [["first"], ["second"]];

        public static IEnumerable<TheoryDataRow<string>> TheoryDataRows =>
            [new TheoryDataRow<string>("first"), new TheoryDataRow<string>("second")];

        public static TheoryData<string> NoRowsAtAll => new();

        [MemberData(nameof(Rows))]
        public void FromTheoryData(string value) { }

        [MemberData(nameof(ObjectArrayRows))]
        public void FromObjectArrays(string value) { }

        [MemberData(nameof(TheoryDataRows))]
        public void FromTheoryDataRows(string value) { }

        [MemberData(nameof(Rows), MemberType = typeof(DataSample))]
        public void FromExplicitMemberType(string value) { }

        [ClassData(typeof(SampleClassData))]
        public void FromClassData(string value) { }

        [InlineData("first")]
        [InlineData("second")]
        public void FromInlineData(string value) { }

        [MemberData(nameof(NoRowsAtAll))]
        public void FromEmptySource(string value) { }

        public void NoRows() { }

        // The trailing parameter comes from the container, not from the row.

        [MemberData(nameof(Rows))]
        public void TheoryDataWithContainerParameter(string value, ContainerSupplied supplied) { }

        [MemberData(nameof(ObjectArrayRows))]
        public void ObjectArraysWithContainerParameter(string value, ContainerSupplied supplied) { }

        [MemberData(nameof(TheoryDataRows))]
        public void TheoryDataRowsWithContainerParameter(string value, ContainerSupplied supplied) { }

        [ClassData(typeof(SampleClassData))]
        public void ClassDataWithContainerParameter(string value, ContainerSupplied supplied) { }

        [InlineData("first")]
        [InlineData("second")]
        public void InlineDataWithContainerParameter(string value, ContainerSupplied supplied) { }
    }

    /// <summary>
    /// Stands in for the service a real module would register. Concrete and parameterless so the
    /// resolver can supply it without any module being loaded.
    /// </summary>
    private class ContainerSupplied { }
#pragma warning restore xUnit1008, xUnit1037

    private class SampleClassData : TheoryData<string> {
        public SampleClassData() {
            Add("first");
            Add("second");
        }
    }
}
