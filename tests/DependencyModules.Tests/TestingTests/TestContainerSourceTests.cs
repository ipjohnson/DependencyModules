using DependencyModules.NSubstitute;
using DependencyModules.Runtime.Attributes;
using DependencyModules.Testing.Attributes;
using DependencyModules.Testing.Attributes.Interfaces;
using DependencyModules.xUnit.Attributes;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace DependencyModules.Tests.TestingTests;

/// <summary>
/// A test that builds containers of its own, and what crosses between them.
/// </summary>
/// <remarks>
/// The whole of the contract in one file: everything is rebuilt, pinned services are not, and both
/// halves of that are asserted against real containers rather than against the collector's answer.
/// </remarks>
[DependencyModule]
public partial class ContainerSourceModule { }

public interface ICounter {
    int Value { get; }

    void Bump();
}

[SingletonService]
public class Counter : ICounter {
    public int Value { get; private set; }

    public void Bump() => Value++;
}

public interface IAudit {
    void Record(string what);
}

[NSubstituteSupport]
public class TestContainerSourceTests {

    /// <summary>
    /// A parameter the test holds crosses every container, with nothing said about it.
    /// </summary>
    /// <remarks>
    /// The row an earlier rule got wrong, and the shape of the scaffolded test that caught it: a
    /// plain application class a handler writes to and the test reads. Pinning it is the default
    /// because a parameter exists to be looked at, and looking at one container's instance while the
    /// work ran in another says nothing.
    /// </remarks>
    [ModuleTest(typeof(ContainerSourceModule))]
    public async Task ABareParameterIsTheSameObjectInEveryContainer(
        ITestContainerSource source, ICounter counter) {
        var first = await source.CreateAsync();
        var second = await source.CreateAsync();

        first.GetRequiredService<ICounter>().Bump();
        second.GetRequiredService<ICounter>().Bump();

        Assert.Same(counter, first.GetRequiredService<ICounter>());
        Assert.Equal(2, counter.Value);
    }

    /// <summary>
    /// A singleton is a singleton inside one container and nothing more, which is the property the
    /// whole design turns on.
    /// </summary>
    /// <remarks>
    /// Asserted on a service the test does <em>not</em> take as a parameter, because taking one
    /// pins it. That is the rule, and this is the test that would otherwise quietly stop testing
    /// anything.
    /// </remarks>
    [ModuleTest(typeof(ContainerSourceModule))]
    public async Task EachContainerGetsItsOwnApplicationSingleton(ITestContainerSource source) {
        var first = await source.CreateAsync();
        var second = await source.CreateAsync();

        first.GetRequiredService<ICounter>().Bump();
        first.GetRequiredService<ICounter>().Bump();
        second.GetRequiredService<ICounter>().Bump();

        Assert.Equal(2, first.GetRequiredService<ICounter>().Value);
        Assert.Equal(1, second.GetRequiredService<ICounter>().Value);
        Assert.NotSame(first.GetRequiredService<ICounter>(), second.GetRequiredService<ICounter>());
    }

    /// <summary>
    /// The counterpart, and the reason [Mock] declares itself shared: the substitute the test holds
    /// is the one every container was built against, so what a container did is visible here.
    /// </summary>
    [ModuleTest(typeof(ContainerSourceModule))]
    public async Task AMockIsTheSameObjectInEveryContainer(ITestContainerSource source, [Mock] IAudit audit) {
        var first = await source.CreateAsync();
        var second = await source.CreateAsync();

        Assert.Same(audit, first.GetRequiredService<IAudit>());
        Assert.Same(audit, second.GetRequiredService<IAudit>());

        first.GetRequiredService<IAudit>().Record("one");
        second.GetRequiredService<IAudit>().Record("two");

        Received.InOrder(() => {
            audit.Record("one");
            audit.Record("two");
        });
    }

    /// <summary>
    /// The container the test itself resolves from is one of the set, not something beside it.
    /// </summary>
    [ModuleTest(typeof(ContainerSourceModule))]
    public async Task TheTestsOwnContainerHoldsTheSameMock(
        ITestContainerSource source, IServiceProvider own, [Mock] IAudit audit) {
        var built = await source.CreateAsync();

        Assert.Same(audit, own.GetRequiredService<IAudit>());
        Assert.Same(audit, built.GetRequiredService<IAudit>());
        Assert.NotSame(own, built);
    }

    /// <summary>
    /// Asking twice gives two containers. Nothing caches, because a caller asking again is a caller
    /// that wants a cold one.
    /// </summary>
    [ModuleTest(typeof(ContainerSourceModule))]
    public async Task EveryCallBuildsAContainer(ITestContainerSource source) {
        var first = await source.CreateAsync();
        var second = await source.CreateAsync();

        Assert.NotSame(first, second);
    }

    /// <summary>
    /// [Shared] on a value parameter is redundant rather than wrong, and says the same thing the
    /// default already says.
    /// </summary>
    /// <remarks>
    /// Kept working on purpose. The attribute earns its place on a parameter that drives the
    /// application, where it asks for one container across the calls, and a reader should not have
    /// to know which kind of parameter they are looking at before writing it.
    /// </remarks>
    [ModuleTest(typeof(ContainerSourceModule))]
    public async Task SharedOnAValueParameterIsRedundant(ITestContainerSource source, [Shared] ICounter counter) {
        var first = await source.CreateAsync();
        var second = await source.CreateAsync();

        first.GetRequiredService<ICounter>().Bump();
        second.GetRequiredService<ICounter>().Bump();

        Assert.Same(counter, first.GetRequiredService<ICounter>());
        Assert.Same(counter, second.GetRequiredService<ICounter>());
        Assert.Equal(2, counter.Value);
    }
}

/// <summary>
/// An export is per container until the use site says otherwise.
/// </summary>
/// <remarks>
/// The pair below is the whole argument for the default. Both read perfectly well, which is why
/// neither is chosen for the test: isolating an export is coherent rather than broken, so it does not
/// clear the bar for sharing without a word at the use site.
/// </remarks>
[NSubstituteSupport]
[TestExport(typeof(ICounter), Implementation = typeof(Counter), Lifetime = ServiceLifetime.Singleton)]
public class IsolatedTestExportTests {

    [ModuleTest]
    public async Task AnExportIsRebuiltWithEachContainer(ITestContainerSource source) {
        var first = await source.CreateAsync();
        var second = await source.CreateAsync();

        first.GetRequiredService<ICounter>().Bump();

        Assert.NotSame(first.GetRequiredService<ICounter>(), second.GetRequiredService<ICounter>());
        Assert.Equal(1, first.GetRequiredService<ICounter>().Value);
        Assert.Equal(0, second.GetRequiredService<ICounter>().Value);
    }
}

[NSubstituteSupport]
[TestExport(typeof(ICounter), Implementation = typeof(Counter), Lifetime = ServiceLifetime.Singleton,
    Shared = true)]
public class SharedTestExportTests {

    [ModuleTest]
    public async Task AnExportAskingToBeSharedCrossesEveryContainer(ITestContainerSource source) {
        var first = await source.CreateAsync();
        var second = await source.CreateAsync();

        first.GetRequiredService<ICounter>().Bump();
        second.GetRequiredService<ICounter>().Bump();

        Assert.Same(first.GetRequiredService<ICounter>(), second.GetRequiredService<ICounter>());
        Assert.Equal(2, first.GetRequiredService<ICounter>().Value);
    }

    /// <summary>
    /// Shared wins over Lifetime, because keeping one object is an instance registration whatever the
    /// registration said.
    /// </summary>
    [ModuleTest]
    [TestExport(typeof(IAudit), Implementation = typeof(RecordingAudit),
        Lifetime = ServiceLifetime.Transient, Shared = true)]
    public async Task SharedOverridesATransientLifetime(ITestContainerSource source) {
        var built = await source.CreateAsync();

        Assert.Same(built.GetRequiredService<IAudit>(), built.GetRequiredService<IAudit>());
    }
}

public class RecordingAudit : IAudit {
    public List<string> Records { get; } = [];

    public void Record(string what) => Records.Add(what);
}

