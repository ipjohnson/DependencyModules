using System.Linq;
using DependencyModules.Tests.Infrastructure;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// DM0018. Modules de-duplicate by type, which is right for a module with no parameters and wrong
/// for one with them: two instances carrying different values are the same module by that rule, so
/// the first reached wins and the other is discarded with nothing said.
///
/// The generator has to pick an identity either way. This reports that it picked, so the choice is
/// the developer's.
/// </summary>
public class ModuleEqualityDiagnosticTests {

    [Fact]
    public void ASettableProperty_IsReported() {
        var result = Run("public int SizeLimit { get; set; }");

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0018");

        Assert.Contains("TestModule", diagnostic.GetMessage());
    }

    /// <summary>
    /// A read-only property is not a parameter. A module implementing an interface with
    /// <c>public string Value =&gt; "A";</c> has nothing anyone can configure, and the generated
    /// attribute never assigns it — so there is no identity question to answer and nothing to report.
    /// </summary>
    [Fact]
    public void AnExpressionBodiedProperty_IsNotReported() {
        var result = Run("""public string Value => "A";""");

        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == "DM0018");
    }

    [Fact]
    public void AGetOnlyProperty_IsNotReported() {
        var result = Run("public string Value { get; } = \"A\";");

        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == "DM0018");
    }

    [Fact]
    public void AStaticProperty_IsNotReported() {
        var result = Run("public static int Shared { get; set; }");

        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == "DM0018");
    }

    [Fact]
    public void NoPropertiesAtAll_IsNotReported() {
        var result = Run("");

        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == "DM0018");
    }

    /// <summary>
    /// Declaring equality is the whole point of the diagnostic, so having done it must silence it.
    /// The generator already stands aside when a module declares its own <c>Equals</c>.
    /// </summary>
    [Fact]
    public void DeclaringEquals_SilencesIt() {
        var result = Run(
            """
            public int SizeLimit { get; set; }

            public override bool Equals(object? obj) =>
                obj is TestModule other && other.SizeLimit == SizeLimit;

            public override int GetHashCode() => SizeLimit;
            """);

        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == "DM0018");
    }

    /// <summary>A record gets its equality from the language, so it never faces the question.</summary>
    [Fact]
    public void ARecordModule_IsNotReported() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            [DependencyModule]
            public partial record TestModule {
                public int SizeLimit { get; set; }
            }
            """);

        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == "DM0018");
    }

    private static GeneratorResult Run(string body) =>
        GeneratorTestHarness.Run(
            $$"""
              using DependencyModules.Runtime.Attributes;

              namespace TestNamespace;

              [DependencyModule]
              public partial class TestModule {
                  {{body}}
              }
              """);
}
