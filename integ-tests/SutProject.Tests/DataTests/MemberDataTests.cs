using DependencyModules.xUnit.Attributes;
using Xunit;

namespace SutProject.Tests.DataTests;

/// <summary>
/// The row sources that are not [InlineData], run end to end through [ModuleTest].
///
/// These shipped broken: [MemberData] resolves its member off ITypeAwareDataAttribute.MemberType,
/// which xUnit back-fills on a path this integration does not take, and left null it yields an
/// empty row collection instead of throwing. Every one of these methods produced zero test cases
/// and the suite reported a pass — which is precisely why asserting inside them was not enough and
/// this file needs to exist. A regression here is only visible as the suite getting smaller, so
/// the unit-level counterpart in ModuleTestCaseDataTests asserts on the number of cases created.
/// </summary>
public class MemberDataTests {

    public static TheoryData<string> Rows => new("one", "two");

    public static IEnumerable<object[]> RawRows() {
        yield return ["one"];
        yield return ["two"];
    }

    public static IEnumerable<TheoryDataRow<string>> TypedRows() {
        yield return new TheoryDataRow<string>("one");
        yield return new TheoryDataRow<string>("two");
    }

    // xUnit1037 counts the row's arguments against the method's parameters and finds them short.
    // Under [ModuleTest] that is the feature — the trailing parameters come from the container.
#pragma warning disable xUnit1037

    [ModuleTest]
    [MemberData(nameof(Rows))]
    [SutModule]
    public void TheoryDataRowsAreSupplied(string value, IDependencyOne one) {
        Assert.NotNull(value);
        Assert.NotNull(one);
    }

    [ModuleTest]
    [MemberData(nameof(RawRows))]
    [SutModule]
    public void ObjectArrayRowsAreSupplied(string value, IDependencyOne one) {
        Assert.NotNull(value);
        Assert.NotNull(one);
    }

    [ModuleTest]
    [MemberData(nameof(TypedRows))]
    [SutModule]
    public void TheoryDataRowRowsAreSupplied(string value, IDependencyOne one) {
        Assert.NotNull(value);
        Assert.NotNull(one);
    }

    /// <summary>
    /// The shape that kept working, kept as a control: an explicit MemberType needs no back-fill.
    /// </summary>
    [ModuleTest]
    [MemberData(nameof(Rows), MemberType = typeof(MemberDataTests))]
    [SutModule]
    public void ExplicitMemberTypeRowsAreSupplied(string value, IDependencyOne one) {
        Assert.NotNull(value);
        Assert.NotNull(one);
    }

    [ModuleTest]
    [ClassData(typeof(ClassRows))]
    [SutModule]
    public void ClassDataRowsAreSupplied(string value, IDependencyOne one) {
        Assert.NotNull(value);
        Assert.NotNull(one);
    }

#pragma warning restore xUnit1037
}

public class ClassRows : TheoryData<string> {
    public ClassRows() {
        Add("one");
        Add("two");
    }
}
