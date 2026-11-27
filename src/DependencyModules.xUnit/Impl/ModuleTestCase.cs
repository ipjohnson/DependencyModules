using DependencyModules.Runtime.Interfaces;
using DependencyModules.Testing.Attributes.Interfaces;
using DependencyModules.Testing.Impl;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Sdk;
using Xunit.v3;

namespace DependencyModules.xUnit.Impl;

/// <summary>
/// Represents a specialized implementation of <see cref="XunitTestCase"/>
/// tailored for module-based test scenarios within the xUnit framework.
/// </summary>
/// <remarks>
/// Self-executing, so that the container a test ran against is disposed when the test case has
/// run. The provider used to go into the case's <see cref="XunitTestCase.DisposalTracker"/>, and
/// xUnit disposes a test case only once every case in the assembly has run - in
/// <c>InProcessFrontController.FindAndRun</c>, after <c>Run</c> returns - so every container a run
/// built stayed alive, with every singleton in it, until the run ended. NUnit's
/// <c>ModuleTestCommand</c> has always disposed in a <c>finally</c> around the test; this is the
/// same lifetime for xUnit.
/// </remarks>
public partial class ModuleTestCase : XunitTestCase, ISelfExecutingXunitTestCase
{
    /// <summary>
    /// One per container this case built: one for a plain test, one per row for a data-driven
    /// one. Runtime state only, never serialized with the case.
    /// </summary>
    private readonly List<ModuleTestContainer> _containers = [];

    // Here as well as on ModuleTestAttribute, because an attribute of another library can name
    // ModuleTestDiscoverer, and then this is the first of the package's types that xUnit creates.
    static ModuleTestCase() => XunitCurrentTestProvider.Install();

#pragma warning disable CS0618 // Type or member is obsolete
    /// <summary>
    /// Represents a specialized implementation of <see cref="XunitTestCase"/>
    /// designed to support module-based test scenarios within the xUnit testing framework.
    /// </summary>
    public ModuleTestCase() { }
#pragma warning restore CS0618 // Type or member is obsolete

    /// <summary>
    /// Represents a specialized implementation of <see cref="XunitTestCase"/>
    /// tailored for module-based test scenarios within the xUnit framework.
    /// </summary>
    public ModuleTestCase(
        IXunitTestMethod testMethod,
        string testCaseDisplayName,
        string uniqueID,
        bool @explicit,
        Type[]? skipExceptions = null,
        string? skipReason = null,
        Type? skipType = null,
        string? skipUnless = null,
        string? skipWhen = null,
        Dictionary<string, HashSet<string>>? traits = null,
        object?[]? testMethodArguments = null,
        string? sourceFilePath = null,
        int? sourceLineNumber = null,
        int? timeout = null
    )
        : base(
            // Named rather than positional throughout. XunitTestCase's constructor takes thirteen
            // parameters, eleven of them optional, and a version that inserts one mid-list rebinds
            // every argument after it — silently where the types happen to line up, and as a wall of
            // unrelated-looking conversion errors where they do not. Named arguments make an insertion
            // either invisible or a single precise error.
            testMethod: testMethod,
            testCaseDisplayName: testCaseDisplayName,
            uniqueID: uniqueID,
            @explicit: @explicit,
            skipExceptions: skipExceptions,
            skipReason: skipReason,
            skipType: skipType,
            skipUnless: skipUnless,
            skipWhen: skipWhen,
            traits: traits,
            testMethodArguments: testMethodArguments,
            sourceFilePath: sourceFilePath,
            sourceLineNumber: sourceLineNumber,
            timeout: timeout
        ) { }

    /// <summary>
    /// Executes logic before the invocation of the test method associated with the current test case.
    /// Override this method to introduce custom pre-invocation behaviors specific to derived test case implementations.
    /// </summary>
    public override void PreInvoke() { }

    /// <summary>
    /// Builds one container for this case. A data-driven test builds one for every row.
    /// </summary>
    private async Task<ModuleTestContainer> BuildContainerAsync()
    {
        var serviceCollection = new ServiceCollection();

        var knownAttributes = TestMethod.Method.GetTestAttributes<Attribute>().ToArray();

        var context = new XunitTestMethodContext(TestMethod, knownAttributes);

        SetupTestCaseInfo(serviceCollection, knownAttributes);

        var container = await ModuleTestComposition.BuildAsync(
            services: serviceCollection,
            modules: CollectModules(knownAttributes),
            testMethod: context,
            overrides: [],
            currentTest: null,
            parent: null
        );

        // Kept here rather than handed to DisposalTracker, which xUnit empties at the end of the
        // run; see the remarks on the class.
        _containers.Add(container);

        return container;
    }

    private void SetupTestCaseInfo(ServiceCollection serviceCollection, Attribute[] knownAttributes)
    {
        serviceCollection.AddSingleton<ITestCaseInfo>(provider =>
            provider.GetRequiredService<TestCaseInfo>()
        );
        serviceCollection.AddSingleton<TestCaseInfo>(_ => new TestCaseInfo(
            TestMethod,
            ArraySegment<object>.Empty,
            knownAttributes
        ));
    }

    /// <summary>
    /// The modules in declaration order: those of <c>[ModuleTest]</c> first, then the module
    /// attributes, widest scope first. <see cref="ModuleTestComposition.BuildAsync"/> reverses them.
    /// </summary>
    private IReadOnlyList<IDependencyModule> CollectModules(Attribute[] knownAttributes)
    {
        var modules = new List<IDependencyModule>();

        // The interface rather than ModuleTestAttribute, so this reads the same for any integration.
        var testAttribute = TestMethod.Method.GetTestAttribute<IModuleTestAttribute>();

        if (testAttribute != null)
        {
            foreach (var moduleType in testAttribute.ModuleTypes)
            {
                if (Activator.CreateInstance(moduleType, []) is IDependencyModule moduleInstance)
                {
                    modules.Add(moduleInstance);
                }
            }
        }

        foreach (var loadModuleAttribute in knownAttributes.OfType<IDependencyModuleProvider>())
        {
            modules.Add(loadModuleAttribute.GetModule());
        }

        return modules;
    }

    private async ValueTask DisposeContainers()
    {
        var containers = _containers.ToArray();

        _containers.Clear();

        foreach (var container in containers)
        {
            await container.DisposeAsync();
        }
    }

    /// <remarks>
    ///     By default, this method returns a single <see cref="XunitTest" /> that is appropriate
    ///     for a one-to-one mapping between test and test case. Override this method to change the
    ///     tests that are associated with this test case.
    /// </remarks>
    /// <inheritdoc />
    public override async ValueTask<IReadOnlyCollection<IXunitTest>> CreateTests()
    {
        var dataAttributes = TestMethod.Method.GetTestAttributes<IDataAttribute>().ToArray();

        if (dataAttributes.Length == 0)
        {
            return await UnitTestWithNoDataAttributes();
        }

        SupplyReflectedType(dataAttributes);

        return await UnitTestFromDataAttributes(dataAttributes);
    }

    /// <summary>
    /// Tells every type-aware data attribute which type it was found on.
    /// </summary>
    /// <remarks>
    /// xUnit does this in <c>ExtensibilityPointFactory.GetMethodDataAttributes</c>, and
    /// <see cref="ITypeAwareDataAttribute"/>'s own documentation makes it the obligation of any
    /// framework that discovers data attributes some other way — which this one does, because it
    /// also sweeps the assembly and the declaring type for the attributes that compose the module.
    ///
    /// Skipping it was silent in the worst way. <c>[MemberData]</c> resolves its member against
    /// <see cref="ITypeAwareDataAttribute.MemberType"/>, and left null it returns an *empty* row
    /// collection rather than throwing — so every row vanished, the test case produced no tests,
    /// and the run reported a pass. Only <c>[MemberData(…, MemberType = typeof(X))]</c>, which
    /// needs no back-fill, kept working.
    ///
    /// Conditional, as the interface requires: an explicit MemberType is the author's answer and is
    /// never overwritten.
    /// </remarks>
    private void SupplyReflectedType(IDataAttribute[] dataAttributes)
    {
        var reflectedType = TestMethod.Method.ReflectedType;

        if (reflectedType == null)
        {
            return;
        }

        foreach (var typeAware in dataAttributes.OfType<ITypeAwareDataAttribute>())
        {
            typeAware.MemberType ??= reflectedType;
        }
    }

    private async Task<IReadOnlyCollection<IXunitTest>> UnitTestFromDataAttributes(
        IDataAttribute[] dataAttributes
    )
    {
        var unitTests = new List<IXunitTest>();

        foreach (var dataAttribute in dataAttributes)
        {
            var dataRowCollection = await dataAttribute.GetData(TestMethod.Method, DisposalTracker);

            foreach (var theoryDataRow in dataRowCollection)
            {
                var data = theoryDataRow.GetData();

                var container = await BuildContainerAsync();

                unitTests.Add(
                    CreateTest(
                        skipReason: theoryDataRow.Skip ?? SkipReason,
                        // The row's own conditional-skip metadata takes precedence over the case's,
                        // matching how skipReason above already defers to it. These became required
                        // in xunit.v3 3.x; passing the case's values alone would have compiled and
                        // silently ignored [Theory]-style per-row skip conditions.
                        skipType: theoryDataRow.SkipType ?? SkipType,
                        skipUnless: theoryDataRow.SkipUnless ?? SkipUnless,
                        skipWhen: theoryDataRow.SkipWhen ?? SkipWhen,
                        testDisplayName: GetRowDisplayName(theoryDataRow, data),
                        testIndex: unitTests.Count,
                        // The row adds its traits to those of the method and the class, which is
                        // what [Theory] does with the same helper.
                        traits: TestIntrospectionHelper
                            .GetTraits(TestMethod, theoryDataRow)
                            .ToReadOnlyTraits(),
                        timeout: theoryDataRow.Timeout ?? Timeout,
                        testMethodArguments: await ResolveArguments(data, container),
                        row: theoryDataRow,
                        dataAttribute: dataAttribute
                    )
                );
            }
        }

        if (unitTests.Count == 0)
        {
            // Failing rather than returning nothing, which is what xUnit's own delay-enumerated
            // theory does for a theory without data. Returning an empty collection here is reported
            // as a pass, so a row source that stopped producing rows — for any reason, not only the
            // MemberType one above — took its coverage with it and left a green suite behind. The
            // NUnit half of this integration already refuses the equivalent case as NotRunnable.
            //
            // Exceptions thrown from CreateTests are caught and converted into a test case failure,
            // which is the documented way to surface this.
            throw new InvalidOperationException(
                $"No data was found for '{TestMethod.TestClass.TestClassName}.{TestMethod.MethodName}'. "
                    + $"It carries {DescribeAttributes(dataAttributes)}, and every one of them returned no rows. "
                    + "A data-driven test with no rows runs nothing, so it is reported as a failure rather "
                    + "than as a pass."
            );
        }

        return unitTests;
    }

    private static string DescribeAttributes(IDataAttribute[] dataAttributes)
    {
        var names = dataAttributes
            .Select(attribute => "[" + TrimAttributeSuffix(attribute.GetType().Name) + "]")
            .ToArray();

        return names.Length == 1 ? names[0] : string.Join(", ", names);
    }

    private static string TrimAttributeSuffix(string name) =>
        name.EndsWith("Attribute", StringComparison.Ordinal)
            ? name.Substring(0, name.Length - "Attribute".Length)
            : name;

    /// <summary>
    /// Names a data row after its own arguments, the way [Theory] does. Without this every row of
    /// a data-driven module test carries the same display name and the rows cannot be told apart
    /// in test explorers or result files.
    /// </summary>
    /// <remarks>
    /// Only the row's own data is used. The remaining parameters are resolved from the container
    /// at execution time, and naming a test after a service instance would be neither readable
    /// nor stable between runs.
    /// </remarks>
    private string GetRowDisplayName(Xunit.ITheoryDataRow theoryDataRow, object?[] data)
    {
        var baseDisplayName = theoryDataRow.TestDisplayName ?? TestCaseDisplayName;

        return TestMethod.GetDisplayName(
            baseDisplayName: baseDisplayName,
            // New in 3.x. The row may carry its own label, and xUnit folds it into the display name
            // for [Theory]; passing null would compile and quietly drop it for module tests.
            label: theoryDataRow.Label,
            testMethodArguments: data,
            methodGenericTypes: null
        );
    }

    private async Task<IReadOnlyCollection<IXunitTest>> UnitTestWithNoDataAttributes()
    {
        var container = await BuildContainerAsync();

        return
        [
            CreateTest(
                skipReason: SkipReason,
                skipType: SkipType,
                skipUnless: SkipUnless,
                skipWhen: SkipWhen,
                testDisplayName: TestCaseDisplayName,
                testIndex: 0,
                traits: Traits.ToReadOnlyTraits(),
                timeout: Timeout,
                testMethodArguments: await ResolveArguments([], container),
                row: null,
                dataAttribute: null
            ),
        ];
    }

    /// <remarks>
    /// The arguments are published on <see cref="TestCaseInfo"/> so a test can read what it was
    /// invoked with. That is xUnit's own object, which is why this is not part of the shared resolver.
    /// </remarks>
    private static async Task<object?[]> ResolveArguments(
        object?[] data,
        ModuleTestContainer container
    )
    {
        var arguments = await container.ResolveArgumentsAsync(data);

        container.Services.GetRequiredService<TestCaseInfo>().TestMethodArguments = arguments;

        return arguments;
    }
}
