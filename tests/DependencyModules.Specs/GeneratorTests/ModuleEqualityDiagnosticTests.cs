using System.Linq;
using DependencyModules.Tests.Infrastructure;

namespace DependencyModules.Specs.GeneratorTests;

/// <summary>
/// DM0018. Modules de-duplicate by type, which is right for a module with no parameters and wrong
/// for one with them: two instances carrying different values are the same module by that rule, so
/// the first reached wins and the other is discarded with nothing said.
///
/// The generator has to pick an identity either way. This reports that it picked, so the choice is
/// the developer's.
/// </summary>
public partial class ModuleEqualityDiagnosticTests
{
    [VerifyTest]
    public void ASettableProperty_IsReported()
    {
        var result = Run("public int SizeLimit { get; set; }");

        var diagnostic = Single(result.GeneratorDiagnostics, d => d.Id == "DM0018");

        Verify(diagnostic.GetMessage().Contains("TestModule"));
    }

    /// <summary>
    /// A read-only property is not a parameter. A module implementing an interface with
    /// <c>public string Value =&gt; "A";</c> has nothing anyone can configure, and the generated
    /// attribute never assigns it — so there is no identity question to answer and nothing to report.
    /// </summary>
    [VerifyTest]
    public void AnExpressionBodiedProperty_IsNotReported()
    {
        var result = Run("""public string Value => "A";""");

        Verify(!result.GeneratorDiagnostics.Any(d => d.Id == "DM0018"));
    }

    [VerifyTest]
    public void AGetOnlyProperty_IsNotReported()
    {
        var result = Run("public string Value { get; } = \"A\";");

        Verify(!result.GeneratorDiagnostics.Any(d => d.Id == "DM0018"));
    }

    [VerifyTest]
    public void AStaticProperty_IsNotReported()
    {
        var result = Run("public static int Shared { get; set; }");

        Verify(!result.GeneratorDiagnostics.Any(d => d.Id == "DM0018"));
    }

    [VerifyTest]
    public void NoPropertiesAtAll_IsNotReported()
    {
        var result = Run("");

        Verify(!result.GeneratorDiagnostics.Any(d => d.Id == "DM0018"));
    }

    /// <summary>
    /// Declaring equality is the whole point of the diagnostic, so having done it must silence it.
    /// The generator already stands aside when a module declares its own <c>Equals</c>.
    /// </summary>
    [VerifyTest]
    public void DeclaringEquals_SilencesIt()
    {
        var result = Run(
            """
            public int SizeLimit { get; set; }

            public override bool Equals(object? obj) =>
                obj is TestModule other && other.SizeLimit == SizeLimit;

            public override int GetHashCode() => SizeLimit;
            """
        );

        Verify(!result.GeneratorDiagnostics.Any(d => d.Id == "DM0018"));
    }

    /// <summary>A record gets its equality from the language, so it never faces the question.</summary>
    [VerifyTest]
    public void ARecordModule_IsNotReported()
    {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            [DependencyModule]
            public partial record TestModule {
                public int SizeLimit { get; set; }
            }
            """
        );

        Verify(!result.GeneratorDiagnostics.Any(d => d.Id == "DM0018"));
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
            """
        );
}
