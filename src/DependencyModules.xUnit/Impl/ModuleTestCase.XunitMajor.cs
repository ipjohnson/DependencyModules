using Xunit.Sdk;
using Xunit.v3;

namespace DependencyModules.xUnit.Impl;

// The members of ModuleTestCase whose xUnit signatures differ between xunit.v3 majors, and nothing
// else. DependencyModules.xUnit compiles this file against 3.x. DependencyModules.xUnit4 compiles the
// same sources against 4.x with XUNIT_V4 defined. A later major adds a branch here and a project of
// its own.
public partial class ModuleTestCase
{
#if XUNIT_V4
    /// <summary>
    /// Runs the case the way xUnit would have, and disposes every container it built once the
    /// run has returned - the tests passed, failed, were skipped or were cancelled alike.
    /// </summary>
    /// <remarks>
    /// <c>XunitRunnerHelper.RunXunitTestCase</c> is what the method runner calls for a case that
    /// does not execute itself: it creates the tests, turns a failure or a dynamic skip during
    /// creation into the case's result, and hands the tests to <see cref="XunitTestCaseRunner"/>.
    /// Wrapping that call is the whole of the difference. Disposal is per case, which for every
    /// test but a data-driven one is per test; the rows of a data-driven test share the case and
    /// are released together when the last has run.
    /// </remarks>
    public async ValueTask<RunSummary> Run(
        ExplicitOption explicitOption,
        IMessageBus messageBus,
        object?[] constructorArguments,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource,
        ParallelMode parallelMode,
        ExecutionScheduler scheduler,
        FixtureMappingManager methodFixtureMappings
    )
    {
        try
        {
            return await XunitRunnerHelper.RunXunitTestCase(
                testCase: this,
                messageBus: messageBus,
                cancellationTokenSource: cancellationTokenSource,
                parallelMode: parallelMode,
                scheduler: scheduler,
                aggregator: aggregator,
                explicitOption: explicitOption,
                constructorArguments: constructorArguments,
                methodFixtureMappings: methodFixtureMappings
            );
        }
        finally
        {
            await DisposeProviders();
        }
    }
#else
    /// <summary>
    /// Runs the case the way xUnit would have, and disposes every container it built once the
    /// run has returned - the tests passed, failed, were skipped or were cancelled alike.
    /// </summary>
    /// <remarks>
    /// <see cref="XunitRunnerHelper.RunXunitTestCase"/> is what the method runner calls for a
    /// case that does not execute itself: it creates the tests, turns a failure or a dynamic skip
    /// during creation into the case's result, and hands the tests to
    /// <see cref="XunitTestCaseRunner"/>. Wrapping that call is the whole of the difference.
    /// Disposal is per case, which for every test but a data-driven one is per test; the rows of
    /// a data-driven test share the case and are released together when the last has run.
    /// </remarks>
    public async ValueTask<RunSummary> Run(
        ExplicitOption explicitOption,
        IMessageBus messageBus,
        object?[] constructorArguments,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource
    )
    {
        try
        {
            return await XunitRunnerHelper.RunXunitTestCase(
                testCase: this,
                messageBus: messageBus,
                cancellationTokenSource: cancellationTokenSource,
                aggregator: aggregator,
                explicitOption: explicitOption,
                constructorArguments: constructorArguments
            );
        }
        finally
        {
            await DisposeProviders();
        }
    }
#endif

    /// <summary>
    /// Creates one test of this case from values already resolved against the case.
    /// </summary>
    /// <remarks>
    /// <paramref name="row"/> and <paramref name="dataAttribute"/> are the data row the test comes
    /// from and the attribute that gave it, or null for a test without data. 4.x reads a label and
    /// a parallelization choice from them, which 3.x does not have.
    ///
    /// Named throughout: <see cref="XunitTest"/> has a second constructor that takes a uniqueID
    /// string near where this passes testIndex, and positionally the two are told apart by the
    /// argument's type alone.
    /// </remarks>
    private XunitTest CreateTest(
        string? skipReason,
        Type? skipType,
        string? skipUnless,
        string? skipWhen,
        string testDisplayName,
        int testIndex,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> traits,
        int timeout,
        object?[] testMethodArguments,
        Xunit.ITheoryDataRow? row,
        IDataAttribute? dataAttribute
    )
    {
#if XUNIT_V4
        return new XunitTest(
            testCase: this,
            testMethod: TestMethod,
            @explicit: Explicit,
            skipReason: skipReason,
            skipType: skipType,
            skipUnless: skipUnless,
            skipWhen: skipWhen,
            testDisplayName: testDisplayName,
            testIndex: testIndex,
            traits: traits,
            timeout: timeout,
            testMethodArguments: testMethodArguments,
            // What [Theory] passes for a row and [Fact] for a test without data. Left to the
            // obsolete constructor, a row marked DisableParallelization would run in parallel.
            testLabel: row?.Label ?? TestLabel,
            disableParallelization: row?.DisableParallelization
                ?? dataAttribute?.DisableParallelization
                ?? DisableParallelization
        );
#else
        return new XunitTest(
            testCase: this,
            testMethod: TestMethod,
            @explicit: Explicit,
            skipReason: skipReason,
            skipType: skipType,
            skipUnless: skipUnless,
            skipWhen: skipWhen,
            testDisplayName: testDisplayName,
            testIndex: testIndex,
            traits: traits,
            timeout: timeout,
            testMethodArguments: testMethodArguments
        );
#endif
    }
}
