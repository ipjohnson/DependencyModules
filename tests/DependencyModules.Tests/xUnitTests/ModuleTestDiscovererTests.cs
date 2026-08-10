using System.Reflection;
using DependencyModules.xUnit.Attributes;
using DependencyModules.xUnit.Impl;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

namespace DependencyModules.Tests.xUnitTests;

/// <summary>
/// Drives ModuleTestDiscoverer directly, from plain [Fact] tests.
///
/// This matters more than it looks. Every integration test runs *through* [ModuleTest], so a
/// discoverer that drops test cases makes that suite quietly smaller rather than red — which is
/// exactly how a unique ID collision shipped undetected. Testing the discoverer from outside the
/// framework it provides is the only way those failures become visible.
/// </summary>
public class ModuleTestDiscovererTests {

    /// <summary>
    /// Regression test. Unique IDs used to be the bare method name, so two test classes each
    /// declaring a same-named test produced colliding IDs and xUnit silently discarded one.
    /// </summary>
    [Fact]
    public async Task SameMethodNameInDifferentClasses_ProducesDifferentUniqueIDs() {
        var first = await DiscoverSingle(typeof(FirstSample), nameof(FirstSample.SharedName));
        var second = await DiscoverSingle(typeof(SecondSample), nameof(SecondSample.SharedName));

        Assert.NotEqual(first.UniqueID, second.UniqueID);
    }

    [Fact]
    public async Task SameMethodNameInDifferentClasses_ProducesDifferentDisplayNames() {
        var first = await DiscoverSingle(typeof(FirstSample), nameof(FirstSample.SharedName));
        var second = await DiscoverSingle(typeof(SecondSample), nameof(SecondSample.SharedName));

        Assert.NotEqual(first.TestCaseDisplayName, second.TestCaseDisplayName);
    }

    [Fact]
    public async Task UniqueID_IsNotJustTheMethodName() {
        var testCase = await DiscoverSingle(typeof(FirstSample), nameof(FirstSample.SharedName));

        Assert.NotEqual(nameof(FirstSample.SharedName), testCase.UniqueID);
    }

    [Fact]
    public async Task UniqueID_IsStableAcrossRepeatedDiscovery() {
        var first = await DiscoverSingle(typeof(FirstSample), nameof(FirstSample.SharedName));
        var second = await DiscoverSingle(typeof(FirstSample), nameof(FirstSample.SharedName));

        Assert.Equal(first.UniqueID, second.UniqueID);
    }

    [Fact]
    public async Task DifferentMethodsInOneClass_ProduceDifferentUniqueIDs() {
        var first = await DiscoverSingle(typeof(FirstSample), nameof(FirstSample.SharedName));
        var second = await DiscoverSingle(typeof(FirstSample), nameof(FirstSample.AnotherName));

        Assert.NotEqual(first.UniqueID, second.UniqueID);
    }

    [Fact]
    public async Task DisplayName_IsQualifiedByItsDeclaringClass() {
        var testCase = await DiscoverSingle(typeof(FirstSample), nameof(FirstSample.SharedName));

        Assert.Contains(nameof(FirstSample), testCase.TestCaseDisplayName);
    }

    [Fact]
    public async Task Discovery_ProducesExactlyOneTestCasePerMethod() {
        var cases = await Discover(typeof(FirstSample), nameof(FirstSample.SharedName));

        Assert.Single(cases);
    }

    [Fact]
    public async Task Discovery_ProducesAModuleTestCase() {
        var testCase = await DiscoverSingle(typeof(FirstSample), nameof(FirstSample.SharedName));

        Assert.IsType<ModuleTestCase>(testCase);
    }

    /// <summary>
    /// The whole point of the unique ID is that xUnit uses it to deduplicate. Collecting IDs for
    /// every sample method asserts the set is distinct, which is the property that was violated.
    /// </summary>
    [Fact]
    public async Task EveryDiscoveredTestCase_HasADistinctUniqueID() {
        var ids = new List<string>();

        foreach (var (type, method) in new[] {
                     (typeof(FirstSample), nameof(FirstSample.SharedName)),
                     (typeof(FirstSample), nameof(FirstSample.AnotherName)),
                     (typeof(SecondSample), nameof(SecondSample.SharedName)),
                     (typeof(SecondSample), nameof(SecondSample.AnotherName))
                 }) {
            ids.Add((await DiscoverSingle(type, method)).UniqueID);
        }

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    /// <summary>
    /// The discoverer copies the method's traits into the shape the test case constructor wants.
    /// That conversion is ours rather than xUnit's, and nothing else in the suite looks at a trait,
    /// so without these a botched copy — dropped values, wrong comparer, empty dictionary — would
    /// leave every test green while trait filtering silently stopped working for anyone using
    /// <c>[ModuleTest]</c>.
    /// </summary>
    [Fact]
    public async Task Traits_OnTheTestMethod_ReachTheTestCase() {
        var testCase = await DiscoverSingle(typeof(TraitSample), nameof(TraitSample.Categorised));

        Assert.Contains("Fast", testCase.Traits["Category"]);
    }

    /// <summary>
    /// Trait keys are matched case-insensitively on the discovered case.
    /// </summary>
    /// <remarks>
    /// This pins xUnit's behaviour rather than ours: <see cref="XunitTestCase"/> rebuilds whatever
    /// traits dictionary it is handed under its own ordinal-ignore-case comparer, so the comparer
    /// the discoverer passes cannot affect this. Worth keeping — a future xUnit that stopped
    /// normalising would break <c>--filter</c> for every <c>[ModuleTest]</c> — but it is not what
    /// guards the conversion. Traits_KeepEveryValueOfARepeatedKey and
    /// Traits_SurviveOntoTheCreatedTests do that.
    /// </remarks>
    [Fact]
    public async Task Traits_AreKeyedCaseInsensitively() {
        var testCase = await DiscoverSingle(typeof(TraitSample), nameof(TraitSample.Categorised));

        Assert.True(testCase.Traits.ContainsKey("cAtEgOrY"));
    }

    [Fact]
    public async Task Traits_KeepEveryValueOfARepeatedKey() {
        var testCase = await DiscoverSingle(typeof(TraitSample), nameof(TraitSample.MultiValued));

        Assert.Equal(["one", "two"], testCase.Traits["Category"].OrderBy(value => value));
    }

    /// <summary>
    /// Traits are converted a second time on the way from the test case into each test it creates.
    /// Nothing at discovery level sees that conversion, so it is asserted here.
    /// </summary>
    /// <remarks>
    /// Values only, deliberately. Neither comparer in the conversion is observable from here:
    /// <see cref="XunitTestCase"/> renormalises whatever it is handed, and
    /// <see cref="Xunit.v3.XunitTest"/> copies traits into a fresh case-sensitive dictionary
    /// regardless of the comparer it receives. An earlier version of this test asserted
    /// case-insensitive lookup on the created test and was simply wrong — that has never been true,
    /// under the <c>Xunit.Internal</c> helpers or their replacement.
    /// </remarks>
    [Fact]
    public async Task Traits_SurviveOntoTheCreatedTests() {
        var testCase = (ModuleTestCase)await DiscoverSingle(
            typeof(TraitSample), nameof(TraitSample.Categorised));

        var test = Assert.Single(await testCase.CreateTests());

        Assert.Contains("Fast", test.Traits["Category"]);
    }

    [Fact]
    public async Task Traits_AreNotSharedBetweenTestCases() {
        var categorised = await DiscoverSingle(typeof(TraitSample), nameof(TraitSample.Categorised));
        var untraited = await DiscoverSingle(typeof(FirstSample), nameof(FirstSample.SharedName));

        Assert.False(untraited.Traits.ContainsKey("Category"));
        Assert.True(categorised.Traits.ContainsKey("Category"));
    }

    /// <summary>
    /// A discovered case carries the location the attribute was written at, so a test explorer has
    /// somewhere to navigate to.
    /// </summary>
    /// <remarks>
    /// Two separate things have to hold for this and each used to fail on its own: the attribute has
    /// to capture the location through caller-info parameters, and the discoverer has to forward it
    /// onto the test case. Neither is visible in a passing test run otherwise — a module test with
    /// no source location runs perfectly well and simply cannot be navigated to.
    ///
    /// The attribute is constructed by the helper below rather than sitting on the sample method, so
    /// the location captured is this file. That is the point: it is a real usage site.
    /// </remarks>
    [Fact]
    public async Task TestCase_CarriesTheSourceLocationOfItsAttribute() {
        var testCase = await DiscoverSingle(typeof(FirstSample), nameof(FirstSample.SharedName));

        Assert.EndsWith("ModuleTestDiscovererTests.cs", testCase.SourceFilePath);
        Assert.True(testCase.SourceLineNumber > 0);
    }

    /// <summary>
    /// Naming a single module still captures the source location.
    /// </summary>
    /// <remarks>
    /// This is the overload-resolution claim the design rests on, asserted rather than assumed.
    /// A params array cannot be followed by caller-info parameters, so the single-module case is a
    /// separate overload; it only wins over expanding the params one because C# prefers a normal
    /// form to an expanded one. If that preference ever failed to apply, this case would silently
    /// lose its source location.
    /// </remarks>
    [Fact]
    public void NamingOneModule_StillCapturesTheSourceLocation() {
        var attribute = new ModuleTestAttribute(typeof(FirstSample));

        Assert.Single(attribute.ModuleTypes);
        Assert.EndsWith("ModuleTestDiscovererTests.cs", attribute.SourceFilePath);
    }

    /// <summary>
    /// Naming two or more modules takes the params overload, which cannot capture a location.
    /// </summary>
    /// <remarks>
    /// Pinning a known limitation rather than a desirable behaviour. C# will not accept caller-info
    /// parameters after a params array, so there is no overload that can serve both. Asserted so the
    /// gap stays visible and deliberate: if a later C# or xUnit makes it fixable, this test fails and
    /// says so.
    /// </remarks>
    [Fact]
    public void NamingSeveralModules_FallsBackToTheOverloadWithoutASourceLocation() {
        var attribute = new ModuleTestAttribute(typeof(FirstSample), typeof(SecondSample));

        Assert.Equal(2, attribute.ModuleTypes.Length);
        Assert.Null(attribute.SourceFilePath);
    }

    private static async Task<IXunitTestCase> DiscoverSingle(Type testClass, string methodName) =>
        Assert.Single(await Discover(testClass, methodName));

    private static async Task<IReadOnlyCollection<IXunitTestCase>> Discover(Type testClass, string methodName) {
        var testMethod = BuildTestMethod(testClass, methodName);

        // Supplied directly rather than read off the sample methods: annotating private nested
        // classes with [ModuleTest] makes the xUnit analyzer treat them as test classes.
        return await new ModuleTestDiscoverer().Discover(
            new DiscoveryOptions(), testMethod, new ModuleTestAttribute());
    }

    /// <summary>
    /// The discoverer only reads method display settings, and xUnit falls back to its defaults for
    /// anything unset, so an empty option bag is enough to exercise it.
    /// </summary>
    private class DiscoveryOptions : ITestFrameworkDiscoveryOptions {
        private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);

        public TValue? GetValue<TValue>(string name) =>
            _values.TryGetValue(name, out var value) && value is TValue typed ? typed : default;

        public void SetValue<TValue>(string name, TValue value) => _values[name] = value;

        public string ToJson() => "{}";
    }

    private static IXunitTestMethod BuildTestMethod(Type testClass, string methodName) {
        var assembly = new XunitTestAssembly(testClass.Assembly);
        var collection = new XunitTestCollection(assembly, null, false, "Test collection");
        var xunitClass = new XunitTestClass(testClass, collection);
        var method = testClass.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)!;

        return new XunitTestMethod(xunitClass, method, []);
    }

    private class FirstSample {
        public void SharedName() { }

        public void AnotherName() { }
    }

    private class SecondSample {
        public void SharedName() { }

        public void AnotherName() { }
    }

    private class TraitSample {
        [Trait("Category", "Fast")]
        public void Categorised() { }

        [Trait("Category", "one")]
        [Trait("Category", "two")]
        public void MultiValued() { }
    }
}
