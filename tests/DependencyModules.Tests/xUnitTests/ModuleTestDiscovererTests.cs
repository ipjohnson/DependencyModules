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
}
