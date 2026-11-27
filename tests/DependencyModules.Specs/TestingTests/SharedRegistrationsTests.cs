using System.Reflection;
using DependencyModules.Testing.Attributes;
using DependencyModules.Testing.Attributes.Interfaces;
using DependencyModules.Testing.Impl;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Specs.TestingTests;

/// <summary>
/// Which services a test pins, decided without a container or a test framework in the way.
/// </summary>
/// <remarks>
/// One test per row of the rule, because the rule has been wrong once already and the way it was
/// wrong was a case nobody had written down.
/// </remarks>
public partial class SharedRegistrationsTests
{
    private interface IThing;

    private interface IOther;

    private class Thing : IThing;

    /// <summary>Stands for a façade or a client: something the harness supplies to drive with.</summary>
    private interface IDriver;

    /// <summary>An attribute that declines, which is the only thing worth saying on a parameter.</summary>
    private class NotSharedAttribute : Attribute, ISharedTestRegistration
    {
        public bool Shared => false;
    }

    /// <summary>A harness naming what it supplies, the way the trigger and web attributes do.</summary>
    private class DrivenByAttribute : Attribute, ISharedTestRegistration
    {
        public IReadOnlyList<Type> IsolatedServices(MethodInfo testMethod) =>
            testMethod
                .GetParameters()
                .Where(parameter => parameter.ParameterType == typeof(IDriver))
                .Select(parameter => parameter.ParameterType)
                .ToArray();
    }

    private static IReadOnlyCollection<Type> Collect(string method, params Attribute[] known) =>
        SharedRegistrations.Collect(
            typeof(SharedRegistrationsTests).GetMethod(
                method,
                BindingFlags.NonPublic | BindingFlags.Static
            )!,
            known
        );

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
    [VerifyTest]
    public void AnUnmarkedParameterIsPinned()
    {
        Verify(Collect(nameof(Plain)).SequenceEqual([typeof(IThing)]));
    }

    [VerifyTest]
    public void EveryParameterIsPinned()
    {
        Verify(Collect(nameof(Bare)).SequenceEqual([typeof(IThing), typeof(IOther)]));
    }

    /// <summary>A mock needs no attribute to be pinned, which is what makes the rule general.</summary>
    [VerifyTest]
    public void AMockIsPinnedLikeAnythingElse()
    {
        Verify(Collect(nameof(Mocked)).SequenceEqual([typeof(IThing)]));
    }

    /// <summary>Redundant now, and still allowed: it is how a driving parameter asks for reuse.</summary>
    [VerifyTest]
    public void SharedOnAValueParameterChangesNothing()
    {
        Verify(Collect(nameof(Marked)).SequenceEqual(Collect(nameof(Plain))));
    }

    [VerifyTest]
    public void ATestWithNoParametersPinsNothing()
    {
        Verify(Collect(nameof(None)).Count == 0);
    }

    // ---------------------------------------------------------------- the exceptions

    /// <summary>
    /// The parameter that drives the application is not pinned, because it builds the containers.
    /// </summary>
    [VerifyTest]
    public void AParameterTheHarnessDrivesWithIsNotPinned()
    {
        var pinned = Collect(nameof(Driving), new DrivenByAttribute());

        Verify(pinned.SequenceEqual([typeof(IThing)]));
    }

    /// <summary>
    /// And it stays unpinned even asked to be. Pinning a driver is not a preference that could go
    /// either way; it turns the isolation off while the test believes it is on.
    /// </summary>
    [VerifyTest]
    public void IsolatedWinsOverAnExplicitShared()
    {
        Verify(Collect(nameof(DrivingAndMarked), new DrivenByAttribute()).Count == 0);
    }

    [VerifyTest]
    public void AnAttributeDecliningUnpinsItsParameter()
    {
        Verify(Collect(nameof(Declined)).Count == 0);
    }

    /// <summary>The container itself is the one question pinning cannot answer.</summary>
    [VerifyTest]
    public void TheServiceProviderIsNeverPinned()
    {
        Verify(Collect(nameof(Container)).SequenceEqual([typeof(IThing)]));
    }

    // ---------------------------------------------------------------- what no parameter holds

    /// <summary>
    /// A harness keeps its own per-test services by naming them, since no parameter holds them.
    /// </summary>
    [VerifyTest]
    public void AnAttributeCanPinWhatNoParameterHolds()
    {
        var shared = new TestExportAttribute(typeof(IOther))
        {
            Implementation = typeof(Thing),
            Shared = true,
        };

        Verify(Collect(nameof(Plain), shared).SequenceEqual([typeof(IThing), typeof(IOther)]));
    }

    /// <summary>An export nothing holds stays per container until it asks.</summary>
    [VerifyTest]
    public void AnExportIsNotPinnedUnlessItAsks()
    {
        var isolated = new TestExportAttribute(typeof(IOther)) { Implementation = typeof(Thing) };

        Verify(Collect(nameof(Plain), isolated).SequenceEqual([typeof(IThing)]));
    }

    /// <summary>
    /// The parameter is the narrower statement and wins, which is how every other precedence in the
    /// harness resolves. Isolating it would recreate the bug the rule exists to fix.
    /// </summary>
    [VerifyTest]
    public void AnExportTheTestHoldsIsPinnedEvenWhenItDeclined()
    {
        var isolated = new TestExportAttribute(typeof(IThing)) { Implementation = typeof(Thing) };

        Verify(Collect(nameof(Plain), isolated).SequenceEqual([typeof(IThing)]));
    }
}
