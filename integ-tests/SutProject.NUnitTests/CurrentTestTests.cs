using DependencyModules.NUnit.Attributes;
using DependencyModules.Testing.Attributes.Interfaces;
using DependencyModules.Testing.Impl;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using NUnit.Framework.Internal;

namespace SutProject.NUnitTests;

/// <summary>
/// <c>CurrentTest</c> under NUnit, where the runner package answers from
/// <c>TestExecutionContext.CurrentContext</c>.
/// </summary>
public class CurrentTestTests
{
    private static readonly List<object?> RepeatKeys = [];

    [ModuleTest]
    public void KeyIsNUnitsObjectForTheRunningTest() =>
        Assert.That(CurrentTest.Key, Is.SameAs(TestExecutionContext.CurrentContext.CurrentTest));

    [ModuleTest]
    public void DisplayNameIsTheFullNameOfTheTest() =>
        Assert.That(CurrentTest.DisplayName, Is.EqualTo(TestContext.CurrentContext.Test.FullName));

    [ModuleTest]
    public void AssemblyIsTheAssemblyOfTheTestClass() =>
        Assert.That(CurrentTest.Assembly, Is.SameAs(typeof(CurrentTestTests).Assembly));

    [ModuleTest]
    public void ALineIsWrittenToTheOutputOfTheTest()
    {
        Assert.That(CurrentTest.TryWriteLine("written through CurrentTest"), Is.True);

        Assert.That(
            TestExecutionContext.CurrentContext.CurrentResult.Output,
            Does.Contain("written through CurrentTest")
        );
    }

    [ModuleTest]
    public void TheLoggerProviderWritesToTheOutputOfTheTest()
    {
        new TestOutputLoggerProvider()
            .CreateLogger("Shop.OrderService")
            .LogInformation("Order {Id} accepted", 42);

        Assert.That(
            TestExecutionContext.CurrentContext.CurrentResult.Output,
            Does.Contain("info: Shop.OrderService[0] Order 42 accepted")
        );
    }

    /// <summary>
    /// The container is built inside the iteration, so the test is already running in the hooks
    /// that build it. Under xUnit it is not; the documentation of CurrentTest gives both.
    /// </summary>
    [ModuleTest]
    [RecordCurrentTestAtStartup]
    public void TheTestIsRunningWhileTheContainerIsBuilt()
    {
        Assert.That(RecordCurrentTestAtStartupAttribute.Key, Is.SameAs(CurrentTest.Key));
        Assert.That(
            TestExecutionContext.CurrentContext.CurrentResult.Output,
            Does.Contain("written while the container is built")
        );
    }

    /// <summary>
    /// NUnit makes no object of its own for one iteration, so the key is the test's for all of
    /// them. The documentation of CurrentTest says so; the container is still one per iteration.
    /// </summary>
    [ModuleTest]
    [Repeat(2)]
    public void TheIterationsOfARepeatedTestShareOneKey()
    {
        RepeatKeys.Add(CurrentTest.Key);

        Assert.That(CurrentTest.Key, Is.Not.Null);
        Assert.That(
            RepeatKeys,
            Has.Count.EqualTo(TestExecutionContext.CurrentContext.CurrentRepeatCount + 1)
        );
        Assert.That(RepeatKeys, Is.All.SameAs(RepeatKeys[0]));
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class RecordCurrentTestAtStartupAttribute : Attribute, ITestStartupAttribute
{
    public static object? Key { get; private set; }

    public Task StartupAsync(ITestMethodContext testMethod, IServiceProvider serviceProvider)
    {
        Key = CurrentTest.Key;

        CurrentTest.TryWriteLine("written while the container is built");

        return Task.CompletedTask;
    }
}
