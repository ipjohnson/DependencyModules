using System.Linq;
using DependencyModules.Tests.Infrastructure;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// Which of a module's properties become parameters on its generated attribute.
///
/// The rule was "settable and not static", read off the syntax without ever looking at
/// accessibility. The generated attribute is a separate <c>public partial class</c>, so a private
/// property copied onto it produced <c>CS0122: … is inaccessible due to its protection level</c> in
/// generated code, twice — once on the attribute's own property and once on the assignment back in
/// <c>GetModule()</c> — with no DM diagnostic saying why. A private member's name leaking onto a
/// public attribute is wrong on its own, before the compile error.
///
/// Accessibility is judged from where the generated attribute sits: same assembly, different type.
/// So internal reaches it and private does not, and an unmodified property is private by default.
/// </summary>
public class ModuleParameterAccessibilityTests {

    [Theory]
    [InlineData("private int SizeLimit { get; set; }")]
    [InlineData("protected int SizeLimit { get; set; }")]
    [InlineData("private protected int SizeLimit { get; set; }")]
    [InlineData("int SizeLimit { get; set; }")]
    public void APropertyTheAttributeCannotReach_CompilesCleanly(string property) {
        Run(property).AssertNoErrors();
    }

    [Theory]
    [InlineData("private int SizeLimit { get; set; }")]
    [InlineData("protected int SizeLimit { get; set; }")]
    [InlineData("private protected int SizeLimit { get; set; }")]
    [InlineData("int SizeLimit { get; set; }")]
    public void APropertyTheAttributeCannotReach_IsNotCopiedOntoIt(string property) {
        var attribute = Run(property).SourceContaining("TestModule.Module");

        Assert.DoesNotContain("SizeLimit", attribute);
    }

    /// <summary>
    /// It is not a parameter, so there is no identity question and DM0018 has nothing to report.
    /// Reporting it would have sent the reader to a page describing a property they cannot set.
    /// </summary>
    [Theory]
    [InlineData("private int SizeLimit { get; set; }")]
    [InlineData("protected int SizeLimit { get; set; }")]
    [InlineData("private protected int SizeLimit { get; set; }")]
    [InlineData("int SizeLimit { get; set; }")]
    public void APropertyTheAttributeCannotReach_IsNotReportedAsDM0018(string property) {
        var result = Run(property);

        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == "DM0018");
    }

    /// <summary>
    /// The controls. internal and protected internal are both reachable from a type in the same
    /// assembly, which is what the generated attribute is, so they stay parameters.
    /// </summary>
    [Theory]
    [InlineData("public int SizeLimit { get; set; }")]
    [InlineData("internal int SizeLimit { get; set; }")]
    [InlineData("protected internal int SizeLimit { get; set; }")]
    public void AReachableProperty_IsStillAParameter(string property) {
        var result = Run(property).AssertNoErrors();

        Assert.Contains("SizeLimit", result.SourceContaining("TestModule.Module"));
        Assert.Contains(result.GeneratorDiagnostics, d => d.Id == "DM0018");
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
