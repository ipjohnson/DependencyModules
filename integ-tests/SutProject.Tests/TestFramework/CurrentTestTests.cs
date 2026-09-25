using System.Reflection;
using DependencyModules.Testing.Attributes.Interfaces;
using DependencyModules.Testing.Impl;
using DependencyModules.xUnit.Attributes;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SutProject.Tests.TestFramework;

/// <summary>
/// <c>CurrentTest</c> under xUnit, where the runner package answers from <c>TestContext.Current</c>.
/// </summary>
/// <remarks>
/// SutProject.Tests runs against both xunit.v3 majors, so these hold for DependencyModules.xUnit and
/// for DependencyModules.xUnit4.
/// </remarks>
public class CurrentTestTests
{
    private static readonly Dictionary<int, object> RowKeys = [];

    [ModuleTest]
    public void KeyIsXunitsObjectForTheRunningTest() =>
        Assert.Same(TestContext.Current.Test, CurrentTest.Key);

    [ModuleTest]
    public void DisplayNameIsXunitsDisplayName() =>
        Assert.Equal(TestContext.Current.Test!.TestDisplayName, CurrentTest.DisplayName);

    [ModuleTest]
    public void AssemblyIsTheAssemblyOfTheTestClass() =>
        Assert.Same(typeof(CurrentTestTests).Assembly, CurrentTest.Assembly);

    [ModuleTest]
    public void ALineIsWrittenToTheOutputOfTheTest()
    {
        Assert.True(CurrentTest.TryWriteLine("written through CurrentTest"));

        Assert.Contains(
            "written through CurrentTest",
            TestContext.Current.TestOutputHelper!.Output
        );
    }

    [ModuleTest]
    public void TheLoggerProviderWritesToTheOutputOfTheTest()
    {
        new TestOutputLoggerProvider()
            .CreateLogger("Shop.OrderService")
            .LogInformation("Order {Id} accepted", 42);

        Assert.Contains(
            "info: Shop.OrderService[0] Order 42 accepted",
            TestContext.Current.TestOutputHelper!.Output
        );
    }

    /// <summary>
    /// Each row is a test of its own, so per-test state keyed on one row does not reach another.
    /// </summary>
    [ModuleTest]
    [InlineData(1)]
    [InlineData(2)]
    public void EachDataRowHasAKeyOfItsOwn(int row)
    {
        var key = Assert.IsAssignableFrom<object>(CurrentTest.Key);

        lock (RowKeys)
        {
            Assert.DoesNotContain(RowKeys.Values, earlier => ReferenceEquals(earlier, key));

            RowKeys[row] = key;
        }
    }

    /// <summary>
    /// A background flow keeps the context of the test that started it after that test finishes,
    /// and xUnit's output helper then throws when written to. A logger on such a flow must drop
    /// the line and not throw into the application under test.
    /// </summary>
    /// <remarks>
    /// Two tests in one class, which xUnit runs one after the other in an order it does not
    /// promise. Whichever runs first starts the writer. The second tells it that the first has
    /// finished, and reads what the write returned.
    /// </remarks>
    [ModuleTest]
    public Task ALineWrittenAfterItsTestHasFinishedIsDropped() => WriteAfterTheTestAsync();

    [ModuleTest]
    public Task WhicheverOfTheTwoLateWriteTestsRanFirst() => WriteAfterTheTestAsync();

    private static readonly TaskCompletionSource FirstLateWriteTestFinished = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    private static readonly TaskCompletionSource<bool> LateWrite = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    private static int _lateWriteTestsStarted;

    private static async Task WriteAfterTheTestAsync()
    {
        if (Interlocked.Increment(ref _lateWriteTestsStarted) == 1)
        {
            _ = Task.Run(async () =>
            {
                await FirstLateWriteTestFinished.Task;

                try
                {
                    LateWrite.SetResult(
                        CurrentTest.TryWriteLine("written after the test finished")
                    );
                }
                catch (Exception exception)
                {
                    LateWrite.SetException(exception);
                }
            });

            return;
        }

        FirstLateWriteTestFinished.SetResult();

        Assert.False(await LateWrite.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    /// <summary>
    /// A case builds the container for each of its tests before xUnit runs the first one, so no
    /// test is running yet in the hooks that build it. The documentation of CurrentTest says so.
    /// </summary>
    [ModuleTest]
    [RecordCurrentTestAtStartup]
    public void NoTestIsRunningWhileTheContainerIsBuilt()
    {
        Assert.True(RecordCurrentTestAtStartupAttribute.Recorded);
        Assert.Null(RecordCurrentTestAtStartupAttribute.Key);
        Assert.False(RecordCurrentTestAtStartupAttribute.Wrote);
        Assert.Same(
            typeof(CurrentTestTests).Assembly,
            RecordCurrentTestAtStartupAttribute.Assembly
        );
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class RecordCurrentTestAtStartupAttribute : Attribute, ITestStartupAttribute
{
    public static bool Recorded { get; private set; }

    public static object? Key { get; private set; }

    public static Assembly? Assembly { get; private set; }

    public static bool Wrote { get; private set; }

    public Task StartupAsync(ITestMethodContext testMethod, IServiceProvider serviceProvider)
    {
        Key = CurrentTest.Key;
        Assembly = CurrentTest.Assembly;
        Wrote = CurrentTest.TryWriteLine("written while the container is built");
        Recorded = true;

        return Task.CompletedTask;
    }
}
