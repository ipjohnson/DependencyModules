using System.Reflection;
using DependencyModules.Testing.Attributes;
using DependencyModules.Testing.Attributes.Interfaces;
using DependencyModules.Testing.Impl;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DependencyModules.Tests.TestingTests;

/// <summary>
/// Which services a test pins, decided without a container or a test framework in the way.
/// </summary>
/// <remarks>
/// One test per row of the rule, because the rule has been wrong once already and the way it was
/// wrong was a case nobody had written down.
/// </remarks>
public class SharedRegistrationsTests {

    private interface IThing;

    private interface IOther;

    private class Thing : IThing;

    /// <summary>Stands for a façade or a client: something the harness supplies to drive with.</summary>
    private interface IDriver;

    /// <summary>An attribute that declines, which is the only thing worth saying on a parameter.</summary>
    private class NotSharedAttribute : Attribute, ISharedTestRegistration {
        public bool Shared => false;
    }

    /// <summary>A harness naming what it supplies, the way the trigger and web attributes do.</summary>
    private class DrivenByAttribute : Attribute, ISharedTestRegistration {
        public IReadOnlyList<Type> IsolatedServices(MethodInfo testMethod) =>
            testMethod.GetParameters()
                .Where(parameter => parameter.ParameterType == typeof(IDriver))
                .Select(parameter => parameter.ParameterType)
                .ToArray();
    }

    private static IReadOnlyCollection<Type> Collect(string method, params Attribute[] known) =>
        SharedRegistrations.Collect(
            typeof(SharedRegistrationsTests).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!,
            known);

    private static void Plain(IThing thing) { }

    private static void Bare(IThing thing, IOther other) { }

    private static void Marked([Shared] IThing thing) { }

    private static void Mocked([Mock] IThing thing) { }

    private static void Declined([NotShared] IThing thing) { }

    private static void Driving(IDriver driver, IThing thing) { }

    private static void DrivingAndMarked([Shared] IDriver driver) { }

    private static void Container(IServiceProvider provider, IThing thing) { }

    private static void None() { }

    // ---------------------------------------------------------------- the default

    /// <summary>
    /// The row the first version of this rule got wrong, and the one a scaffolded project hits
    /// first: a plain application service the handler writes to and the test reads.
    /// </summary>
    [Fact]
    public void AnUnmarkedParameterIsPinned() {
        Assert.Equal([typeof(IThing)], Collect(nameof(Plain)));
    }

    [Fact]
    public void EveryParameterIsPinned() {
        Assert.Equal([typeof(IThing), typeof(IOther)], Collect(nameof(Bare)));
    }

    /// <summary>A mock needs no attribute to be pinned, which is what makes the rule general.</summary>
    [Fact]
    public void AMockIsPinnedLikeAnythingElse() {
        Assert.Equal([typeof(IThing)], Collect(nameof(Mocked)));
    }

    /// <summary>Redundant now, and still allowed: it is how a driving parameter asks for reuse.</summary>
    [Fact]
    public void SharedOnAValueParameterChangesNothing() {
        Assert.Equal(Collect(nameof(Plain)), Collect(nameof(Marked)));
    }

    [Fact]
    public void ATestWithNoParametersPinsNothing() {
        Assert.Empty(Collect(nameof(None)));
    }

    // ---------------------------------------------------------------- the exceptions

    /// <summary>
    /// The parameter that drives the application is not pinned, because it builds the containers.
    /// </summary>
    [Fact]
    public void AParameterTheHarnessDrivesWithIsNotPinned() {
        var pinned = Collect(nameof(Driving), new DrivenByAttribute());

        Assert.Equal([typeof(IThing)], pinned);
    }

    /// <summary>
    /// And it stays unpinned even asked to be. Pinning a driver is not a preference that could go
    /// either way; it turns the isolation off while the test believes it is on.
    /// </summary>
    [Fact]
    public void IsolatedWinsOverAnExplicitShared() {
        Assert.Empty(Collect(nameof(DrivingAndMarked), new DrivenByAttribute()));
    }

    [Fact]
    public void AnAttributeDecliningUnpinsItsParameter() {
        Assert.Empty(Collect(nameof(Declined)));
    }

    /// <summary>The container itself is the one question pinning cannot answer.</summary>
    [Fact]
    public void TheServiceProviderIsNeverPinned() {
        Assert.Equal([typeof(IThing)], Collect(nameof(Container)));
    }

    // ---------------------------------------------------------------- what no parameter holds

    /// <summary>
    /// A harness keeps its own per-test services by naming them, since no parameter holds them.
    /// </summary>
    [Fact]
    public void AnAttributeCanPinWhatNoParameterHolds() {
        var shared = new TestExportAttribute(typeof(IOther)) {
            Implementation = typeof(Thing), Shared = true
        };

        Assert.Equal([typeof(IThing), typeof(IOther)], Collect(nameof(Plain), shared));
    }

    /// <summary>An export nothing holds stays per container until it asks.</summary>
    [Fact]
    public void AnExportIsNotPinnedUnlessItAsks() {
        var isolated = new TestExportAttribute(typeof(IOther)) { Implementation = typeof(Thing) };

        Assert.Equal([typeof(IThing)], Collect(nameof(Plain), isolated));
    }

    /// <summary>
    /// The parameter is the narrower statement and wins, which is how every other precedence in the
    /// harness resolves. Isolating it would recreate the bug the rule exists to fix.
    /// </summary>
    [Fact]
    public void AnExportTheTestHoldsIsPinnedEvenWhenItDeclined() {
        var isolated = new TestExportAttribute(typeof(IThing)) { Implementation = typeof(Thing) };

        Assert.Equal([typeof(IThing)], Collect(nameof(Plain), isolated));
    }
}
