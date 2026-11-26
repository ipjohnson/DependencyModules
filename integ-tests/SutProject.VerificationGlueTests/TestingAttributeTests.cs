using System.Reflection;
using DependencyModules.Runtime;
using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using DependencyModules.Testing.Attributes;
using DependencyModules.Testing.Attributes.Interfaces;
using DependencyModules.Testing.Impl;
using DependencyModules.VerificationGlue.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VerificationGlue.Tests;
using static VerificationGlue.VerifySyntax;

namespace SutProject.VerificationGlueTests;

/// <summary>
/// Names the environment, and records the test method it was given.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method)]
public class SeededEnvironmentAttribute(string name) : Attribute, IModuleEnvironmentProvider
{
    public IModuleEnvironment? ProvideEnvironment(MethodInfo testMethod) =>
        new ModuleEnvironment(false, name + ":" + testMethod.Name);
}

public interface IGatedByEnvironment { }

[SingletonService(Realm = typeof(SeededEnvironmentModule))]
[IfEnvironment(
    "seeded:" + nameof(EnvironmentTests.AGatedRegistrationAppliesUnderTheSeededEnvironment)
)]
public class GatedByEnvironment : IGatedByEnvironment { }

[DependencyModule(OnlyRealm = true)]
public partial class SeededEnvironmentModule { }

[ModuleTest<SeededEnvironmentModule>]
public partial class EnvironmentTests
{
    /// <summary>
    /// The environment applies before the modules, and the attribute receives this method.
    /// </summary>
    [VerifyTest]
    [SeededEnvironment("seeded")]
    public void AGatedRegistrationAppliesUnderTheSeededEnvironment(IServiceProvider provider)
    {
        Verify(provider.GetService<IGatedByEnvironment>() is not null);
    }

    [VerifyTest]
    public void TheSameRegistrationIsAbsentWithoutASeed(IServiceProvider provider)
    {
        Verify(provider.GetService<IGatedByEnvironment>() is null);
    }
}

public class StartupRecord
{
    public string? Method { get; set; }
}

/// <summary>
/// Runs against the built container, with the test method in hand.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RecordStartupAttribute : Attribute, ITestStartupAttribute, ITestServiceSetupAttribute
{
    public void SetupServiceCollection(
        ITestMethodContext testMethod,
        IServiceCollection serviceCollection
    ) => serviceCollection.AddSingleton<StartupRecord>();

    public Task StartupAsync(ITestMethodContext testMethod, IServiceProvider serviceProvider)
    {
        serviceProvider.GetRequiredService<StartupRecord>().Method = testMethod.Method.Name;

        return Task.CompletedTask;
    }
}

public class BuiltByAttribute { }

/// <summary>
/// Builds the provider, and leaves a mark in it.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class MarkingProviderBuilderAttribute : Attribute, IServiceProviderBuilderAttribute
{
    public IServiceProvider BuildServiceProvider(
        ITestMethodContext testMethod,
        IServiceCollection serviceCollection
    )
    {
        serviceCollection.AddSingleton<BuiltByAttribute>();

        return serviceCollection.BuildServiceProvider(validateScopes: true);
    }
}

public class Order(IValidator validator, string region)
{
    public IValidator Validator { get; } = validator;

    public string Region { get; } = region;
}

public interface IValidator { }

public class Validator : IValidator { }

[ModuleTest]
[SutModule]
public partial class TestingAttributeTests
{
    [VerifyTest]
    [RecordStartup]
    public void AStartupAttributeRunsWithTheTestMethod(StartupRecord record)
    {
        Verify(record.Method == nameof(AStartupAttributeRunsWithTheTestMethod));
    }

    [VerifyTest]
    [MarkingProviderBuilder]
    public void AProviderBuilderBuildsTheContainer(BuiltByAttribute marker)
    {
        Verify(marker is not null);
    }

    [VerifyTest]
    [TestExport(typeof(IValidator), Implementation = typeof(Validator))]
    public void InjectValuesSuppliesConstructorArguments([InjectValues("wholesale")] Order order)
    {
        Verify(order.Region == "wholesale");
        Verify(order.Validator is Validator);
    }

    /// <summary>
    /// Each test container holds an <see cref="ICurrentTestProvider"/> for its own test.
    /// </summary>
    [VerifyTest]
    public void TheContainerKnowsTheCurrentTest(
        ICurrentTestProvider currentTest,
        TestContext context
    )
    {
        Verify(ReferenceEquals(currentTest.Key, context.Scope));
        Verify(currentTest.DisplayName == context.Uid);
        Verify(currentTest.Assembly == typeof(TestingAttributeTests).Assembly);
        Verify(currentTest.TryWriteLine("written through ICurrentTestProvider"));
    }

    [VerifyTest]
    public void TheTestOutputLoggerWritesThroughIt(ICurrentTestProvider currentTest)
    {
        using var provider = new TestOutputLoggerProvider(currentTest);

        provider.CreateLogger("Test").LogInformation("logged to the test output");

        Verify(currentTest.TryWriteLine("after the log entry"));
    }
}

/// <summary>
/// A test class in a nested type, whose UID names it with a dot.
/// </summary>
public static partial class Outer
{
    [ModuleTest]
    [SutModule]
    public partial class NestedTests
    {
        [VerifyTest]
        [RecordStartup]
        public void FindsTheMethodOfANestedClass(StartupRecord record)
        {
            Verify(record.Method == nameof(FindsTheMethodOfANestedClass));
        }
    }
}
