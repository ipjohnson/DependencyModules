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
public class SharedRegistrationsTests {

    private interface IThing;

    private interface IOther;

    private class Thing : IThing;

    /// <summary>An attribute that answers no, to prove declining is not the same as not implementing.</summary>
    private class NotSharedAttribute : Attribute, ISharedTestRegistration {
        public bool Shared => false;

        public IReadOnlyList<Type> SharedServices => [typeof(IOther)];
    }

    private static IReadOnlyCollection<Type> Collect(string method, params Attribute[] known) =>
        SharedRegistrations.Collect(
            typeof(SharedRegistrationsTests).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!,
            known);

    private static void Plain(IThing thing) { }

    private static void Marked([Shared] IThing thing) { }

    private static void Mocked([Mock] IThing thing) { }

    private static void Declined([NotShared] IThing thing) { }

    private static void Several([Shared] IThing thing, [Mock] IOther other, string plain) { }

    /// <summary>A parameter says nothing on its own, which is what keeps the default isolated.</summary>
    [Fact]
    public void AnUnmarkedParameterIsNotPinned() {
        Assert.Empty(Collect(nameof(Plain)));
    }

    [Fact]
    public void SharedPinsTheParametersType() {
        Assert.Equal([typeof(IThing)], Collect(nameof(Marked)));
    }

    /// <summary>
    /// The point of the interface: [Mock] pins without the use site writing [Shared].
    /// </summary>
    [Fact]
    public void MockPinsWithoutBeingAsked() {
        Assert.Equal([typeof(IThing)], Collect(nameof(Mocked)));
    }

    [Fact]
    public void AnAttributeAnsweringNoPinsNothing() {
        Assert.Empty(Collect(nameof(Declined)));
    }

    [Fact]
    public void EveryMarkedParameterIsCollected() {
        Assert.Equal([typeof(IThing), typeof(IOther)], Collect(nameof(Several)));
    }

    /// <summary>
    /// An attribute with no parameter names what it registered, which is how [TestExport] joins in
    /// from the method, the class or the assembly.
    /// </summary>
    [Fact]
    public void AnExportAskingToBeSharedNamesItsOwnService() {
        var shared = new TestExportAttribute(typeof(IThing)) {
            Implementation = typeof(Thing), Shared = true
        };

        Assert.Equal([typeof(IThing)], Collect(nameof(Plain), shared));
    }

    /// <summary>The default, and the reason the default is what it is.</summary>
    [Fact]
    public void AnExportIsNotPinnedUnlessItAsks() {
        var isolated = new TestExportAttribute(typeof(IThing)) { Implementation = typeof(Thing) };

        Assert.Empty(Collect(nameof(Plain), isolated));
    }

    /// <summary>
    /// Two attributes naming one service and disagreeing is a use site asking for both. Pinning is
    /// the answer that leaves the test able to see what it asked to see.
    /// </summary>
    [Fact]
    public void OneAttributeAskingIsEnough() {
        var isolated = new TestExportAttribute(typeof(IThing)) { Implementation = typeof(Thing) };

        Assert.Equal([typeof(IThing)], Collect(nameof(Marked), isolated));
    }
}
