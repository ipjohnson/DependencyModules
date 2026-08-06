using DependencyModules.Tests.Infrastructure;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// The generator is an IIncrementalGenerator, so its model comparers decide how much work the
/// compiler redoes on every edit. Too strict and the IDE regenerates on each keystroke; too loose
/// and a real change serves stale output. These tests pin that behaviour from the outside, which
/// is more durable than asserting on the comparers directly.
/// </summary>
public class IncrementalGenerationTests {

    [Fact]
    public void RerunningOnUnchangedSource_ReusesCachedOutput() {
        var result = GeneratorTestHarness.RunIncremental(Sources(Service), Sources(Service));

        Assert.True(result.AllOutputsCached,
            "Re-running on identical source recomputed output: " +
            string.Join(", ", result.OutputReasons));
    }

    [Fact]
    public void EditingAnUnrelatedMethodBody_ReusesCachedOutput() {
        var before = Sources(
            Service +
            """

            public class Unrelated {
                public int Compute() => 1;
            }
            """);

        var after = Sources(
            Service +
            """

            public class Unrelated {
                public int Compute() => 2;
            }
            """);

        var result = GeneratorTestHarness.RunIncremental(before, after);

        Assert.Equal(result.FirstRun.Keys.OrderBy(k => k), result.SecondRun.Keys.OrderBy(k => k));
        Assert.True(result.AllOutputsCached,
            "Editing an unrelated method body regenerated output: " +
            string.Join(", ", result.OutputReasons));
    }

    [Fact]
    public void AddingAComment_ReusesCachedOutput() {
        var result = GeneratorTestHarness.RunIncremental(
            Sources(Service),
            Sources("// a new comment\n" + Service));

        Assert.True(result.AllOutputsCached,
            "Adding a comment regenerated output: " + string.Join(", ", result.OutputReasons));
    }

    [Fact]
    public void AddingAService_RegeneratesAndIncludesIt() {
        var after = Sources(
            Service +
            """

            public interface ISecond;

            [SingletonService]
            public class SecondThing : ISecond;
            """);

        var result = GeneratorTestHarness.RunIncremental(Sources(Service), after);

        var dependencies = result.SecondRun.Single(pair => pair.Key.Contains("Dependencies")).Value;

        Assert.Contains("SecondThing", dependencies);
        Assert.DoesNotContain("SecondThing", result.FirstRun.Single(pair => pair.Key.Contains("Dependencies")).Value);
    }

    [Fact]
    public void ChangingAServiceLifetime_RegeneratesWithTheNewLifetime() {
        var before = Sources("[SingletonService]\npublic class Thing : IThing;");
        var after = Sources("[ScopedService]\npublic class Thing : IThing;");

        var result = GeneratorTestHarness.RunIncremental(before, after);

        var first = result.FirstRun.Single(pair => pair.Key.Contains("Dependencies")).Value;
        var second = result.SecondRun.Single(pair => pair.Key.Contains("Dependencies")).Value;

        Assert.Contains("AddSingleton", first);
        Assert.Contains("AddScoped", second);
        Assert.DoesNotContain("AddSingleton", second);
    }

    [Fact]
    public void RemovingAService_RegeneratesWithoutIt() {
        var before = Sources(
            Service +
            """

            public interface ISecond;

            [SingletonService]
            public class SecondThing : ISecond;
            """);

        var result = GeneratorTestHarness.RunIncremental(before, Sources(Service));

        var second = result.SecondRun.Single(pair => pair.Key.Contains("Dependencies")).Value;

        Assert.DoesNotContain("SecondThing", second);
    }

    private const string Service =
        """
        [SingletonService]
        public class Thing : IThing;
        """;

    private static Dictionary<string, string> Sources(string body) =>
        new() {
            ["Test.cs"] =
                $$"""
                  using DependencyModules.Runtime.Attributes;

                  namespace TestNamespace;

                  public interface IThing;

                  {{body}}

                  [DependencyModule]
                  public partial class TestModule;
                  """
        };
}
