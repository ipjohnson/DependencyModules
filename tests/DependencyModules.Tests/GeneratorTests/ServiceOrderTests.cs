using System.Collections.Generic;
using System.Linq;
using DependencyModules.Tests.Infrastructure;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// <c>Order</c> on a service attribute, deciding where an implementation lands in
/// <c>IEnumerable&lt;T&gt;</c>.
///
/// Decorators and interceptors have had an <c>Order</c> since they existed, because nesting is
/// meaningless without one. Registrations did not, and several implementations of one interface came
/// out in whatever order the generator emitted them — which is sorted by class name, so renaming a
/// class reordered a pipeline. Both agents who hit it put an <c>int Order</c> on their own interface
/// and sorted in the consuming loop, which is the workaround this removes.
///
/// The default is 0 for everything, and the sort is stable within an order, so a project that names
/// no orders sees exactly what it saw before.
/// </summary>
public class ServiceOrderTests {

    [Fact]
    public void WithNoOrderNamed_TheExistingOrderIsKept() {
        Assert.Equal(["Alpha", "Beta", "Gamma"], Resolve("", "", ""));
    }

    [Fact]
    public void OrderDecidesTheSequence() {
        Assert.Equal(
            ["Gamma", "Beta", "Alpha"],
            Resolve(", Order = 30", ", Order = 20", ", Order = 10"));
    }

    /// <summary>
    /// Negative orders sort ahead of the unordered majority, which is what "run this one first"
    /// looks like when the rest of the project has never named an order.
    /// </summary>
    [Fact]
    public void ANegativeOrder_SortsAhead() {
        Assert.Equal(["Gamma", "Alpha", "Beta"], Resolve("", "", ", Order = -1"));
    }

    /// <summary>
    /// Within one order the previous rule still decides, so naming an order for some services does
    /// not scramble the rest.
    /// </summary>
    [Fact]
    public void WithinOneOrder_TheSortIsStable() {
        Assert.Equal(["Alpha", "Beta", "Gamma"], Resolve(", Order = 5", ", Order = 5", ", Order = 5"));
    }

    /// <summary>
    /// The container returns the last registration for a single resolve, so ordering decides this
    /// too — worth pinning, because it is the half a reader does not think about.
    /// </summary>
    [Fact]
    public void TheLastInOrder_IsWhatASingleResolveReturns() {
        var generated = Build(", Order = 30", ", Order = 20", ", Order = 10");

        var resolved = generated.BuildProvider().GetService(generated.Type("IStep"))!;

        Assert.Equal("Alpha", resolved.GetType().Name);
    }

    private static string[] Resolve(string alpha, string beta, string gamma) {
        var generated = Build(alpha, beta, gamma);

        return ((System.Collections.IEnumerable)generated.BuildProvider()
                .GetService(typeof(IEnumerable<>).MakeGenericType(generated.Type("IStep")))!)
            .Cast<object>()
            .Select(step => step.GetType().Name)
            .ToArray();
    }

    private static GeneratedAssembly Build(string alpha, string beta, string gamma) =>
        GeneratedAssembly.Create(
            $$"""
              using DependencyModules.Runtime.Attributes;

              namespace TestNamespace;

              public interface IStep;

              [SingletonService(As = typeof(IStep){{alpha}})]
              public class Alpha : IStep;

              [SingletonService(As = typeof(IStep){{beta}})]
              public class Beta : IStep;

              [SingletonService(As = typeof(IStep){{gamma}})]
              public class Gamma : IStep;

              [DependencyModule]
              public partial class TestModule;
              """);
}
