using DependencyModules.Testing.Attributes;
using DependencyModules.VerificationGlue.Attributes;
using Microsoft.Extensions.DependencyInjection;
using VerificationGlue.Mocks;
using VerificationGlue.Tests;
using static VerificationGlue.VerifySyntax;
using MockAttribute = VerificationGlue.Mocks.MockAttribute;

namespace SutProject.VerificationGlueTests;

public interface ICounter
{
    int Next();
}

public class Counter : ICounter
{
    private int _count;

    public int Next() => Interlocked.Increment(ref _count);
}

/// <summary>
/// Records the first instance a test saw, so each later test can compare with it whatever order
/// the tests run in.
/// </summary>
public sealed class FirstSeen
{
    private object? _first;

    public bool IsFirst(object instance) =>
        ReferenceEquals(
            Interlocked.CompareExchange(ref _first, instance, null) ?? instance,
            instance
        );
}

/// <summary>
/// Records every instance the tests saw, so each test can check that no other test saw its own.
/// </summary>
public sealed class SeenOnce
{
    private readonly HashSet<object> _seen = new(ReferenceEqualityComparer.Instance);

    public bool IsNew(object instance)
    {
        lock (_seen)
        {
            return _seen.Add(instance);
        }
    }
}

/// <summary>
/// One container for the class. A <c>[Shared]</c> parameter is the same instance in each test, a
/// parameter without it is built again for each test, and each test has its own mocks.
/// </summary>
[ModuleTest<SutModule>(Scope = ContainerScope.Class)]
public partial class ClassScopeTests
{
    private static readonly FirstSeen SharedSingleton = new();
    private static readonly SeenOnce IsolatedSingleton = new();

    [VerifyTest]
    public void ASharedParameterIsOneInstance([Shared] ISingletonService singletonService)
    {
        Verify(SharedSingleton.IsFirst(singletonService));
    }

    [VerifyTest]
    public void ASharedParameterIsOneInstanceInTheNextTestToo(
        [Shared] ISingletonService singletonService,
        IDependencyOne dependencyOne
    )
    {
        Verify(SharedSingleton.IsFirst(singletonService));
        Verify(ReferenceEquals(dependencyOne.SingletonService, singletonService));
    }

    [VerifyTest]
    public void AParameterWithoutSharedIsBuiltForEachTest(ISingletonService singletonService)
    {
        Verify(IsolatedSingleton.IsNew(singletonService));
    }

    [VerifyTest]
    public void AParameterWithoutSharedIsBuiltForEachTestAgain(ISingletonService singletonService)
    {
        Verify(IsolatedSingleton.IsNew(singletonService));
    }

    [VerifyTest]
    public void EachTestGetsItsOwnMock(
        [Mock] ISingletonService singletonService,
        IDependencyOne dependencyOne
    )
    {
        singletonService.GetName().Returns("class scope");

        Verify(ReferenceEquals(dependencyOne.SingletonService, singletonService));
        Verify(dependencyOne.SingletonService.GetName() == "class scope");
    }
}

/// <summary>
/// An export the class marks shared is one instance in each test of the class.
/// </summary>
[ModuleTest(Scope = ContainerScope.Class)]
[SutModule]
[TestExport(typeof(ICounter), Implementation = typeof(Counter), Shared = true)]
public partial class SharedExportTests
{
    private static readonly FirstSeen SharedCounter = new();

    [VerifyTest]
    public void TheExportIsOneInstance(ICounter counter)
    {
        counter.Next();

        Verify(SharedCounter.IsFirst(counter));
    }

    [VerifyTest]
    public void TheExportIsOneInstanceInTheNextTestToo(ICounter counter, IServiceProvider services)
    {
        counter.Next();

        Verify(SharedCounter.IsFirst(counter));
        Verify(ReferenceEquals(services.GetRequiredService<ICounter>(), counter));
    }

    /// <summary>
    /// A mock replaces a shared export for the test that asks for it.
    /// </summary>
    [VerifyTest]
    public void AMockReplacesTheSharedExport([Mock] ICounter counter, IServiceProvider services)
    {
        Verify(counter is not Counter);
        Verify(ReferenceEquals(services.GetRequiredService<ICounter>(), counter));
    }
}

/// <summary>
/// One container for the assembly. The two classes name the same bootstrap, so they share it.
/// </summary>
[ModuleTest<SutModule>(Scope = ContainerScope.Assembly)]
public partial class AssemblyScopeFirstTests
{
    internal static readonly FirstSeen Shared = new();

    [VerifyTest]
    public void ASharedParameterIsOneInstanceInTheAssembly(
        [Shared] ISingletonService singletonService
    )
    {
        Verify(Shared.IsFirst(singletonService));
    }

    [VerifyTest]
    public void EachTestGetsItsOwnMock(
        [Mock] ISingletonService singletonService,
        IDependencyOne dependencyOne
    )
    {
        Verify(ReferenceEquals(dependencyOne.SingletonService, singletonService));
    }
}

[ModuleTest<SutModule>(Scope = ContainerScope.Assembly)]
public partial class AssemblyScopeSecondTests
{
    [VerifyTest]
    public void ASharedParameterIsOneInstanceInAnotherClass(
        [Shared] ISingletonService singletonService
    )
    {
        Verify(AssemblyScopeFirstTests.Shared.IsFirst(singletonService));
    }
}
