using System.Collections.Generic;
using System.Linq;
using DependencyModules.Tests.Infrastructure;

namespace DependencyModules.Specs.GeneratorTests;

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
public partial class ServiceOrderTests
{
    [VerifyTest]
    public void WithNoOrderNamed_TheExistingOrderIsKept()
    {
        Verify(Resolve("", "", "").SequenceEqual(["Alpha", "Beta", "Gamma"]));
    }

    [VerifyTest]
    public void OrderDecidesTheSequence()
    {
        Verify(
            Resolve(", Order = 30", ", Order = 20", ", Order = 10")
                .SequenceEqual(["Gamma", "Beta", "Alpha"])
        );
    }

    /// <summary>
    /// Negative orders sort ahead of the unordered majority, which is what "run this one first"
    /// looks like when the rest of the project has never named an order.
    /// </summary>
    [VerifyTest]
    public void ANegativeOrder_SortsAhead()
    {
        Verify(Resolve("", "", ", Order = -1").SequenceEqual(["Gamma", "Alpha", "Beta"]));
    }

    /// <summary>
    /// Within one order the previous rule still decides, so naming an order for some services does
    /// not scramble the rest.
    /// </summary>
    [VerifyTest]
    public void WithinOneOrder_TheSortIsStable()
    {
        Verify(
            Resolve(", Order = 5", ", Order = 5", ", Order = 5")
                .SequenceEqual(["Alpha", "Beta", "Gamma"])
        );
    }

    /// <summary>
    /// The container returns the last registration for a single resolve, so ordering decides this
    /// too — worth pinning, because it is the half a reader does not think about.
    /// </summary>
    [VerifyTest]
    public void TheLastInOrder_IsWhatASingleResolveReturns()
    {
        var generated = Build(", Order = 30", ", Order = 20", ", Order = 10");

        var resolved = generated.BuildProvider().GetService(generated.Type("IStep"))!;

        Verify(resolved.GetType().Name == "Alpha");
    }

    private static string[] Resolve(string alpha, string beta, string gamma)
    {
        var generated = Build(alpha, beta, gamma);

        return (
            (System.Collections.IEnumerable)
                generated
                    .BuildProvider()
                    .GetService(typeof(IEnumerable<>).MakeGenericType(generated.Type("IStep")))!
        )
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
            """
        );
}
