using System.Linq;
using DependencyModules.Tests.Infrastructure;

namespace DependencyModules.Specs.GeneratorTests;

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
public partial class ModuleParameterAccessibilityTests
{
    [VerifyTest]
    [Row("private int SizeLimit { get; set; }")]
    [Row("protected int SizeLimit { get; set; }")]
    [Row("private protected int SizeLimit { get; set; }")]
    [Row("int SizeLimit { get; set; }")]
    public void APropertyTheAttributeCannotReach_CompilesCleanly(string property)
    {
        Run(property).AssertNoErrors();
    }

    [VerifyTest]
    [Row("private int SizeLimit { get; set; }")]
    [Row("protected int SizeLimit { get; set; }")]
    [Row("private protected int SizeLimit { get; set; }")]
    [Row("int SizeLimit { get; set; }")]
    public void APropertyTheAttributeCannotReach_IsNotCopiedOntoIt(string property)
    {
        var attribute = Run(property).SourceContaining("TestModule.Module");

        Verify(!attribute.Contains("SizeLimit"));
    }

    /// <summary>
    /// It is not a parameter, so there is no identity question and DM0018 has nothing to report.
    /// Reporting it would have sent the reader to a page describing a property they cannot set.
    /// </summary>
    [VerifyTest]
    [Row("private int SizeLimit { get; set; }")]
    [Row("protected int SizeLimit { get; set; }")]
    [Row("private protected int SizeLimit { get; set; }")]
    [Row("int SizeLimit { get; set; }")]
    public void APropertyTheAttributeCannotReach_IsNotReportedAsDM0018(string property)
    {
        var result = Run(property);

        Verify(!result.GeneratorDiagnostics.Any(d => d.Id == "DM0018"));
    }

    /// <summary>
    /// The controls. internal and protected internal are both reachable from a type in the same
    /// assembly, which is what the generated attribute is, so they stay parameters.
    /// </summary>
    [VerifyTest]
    [Row("public int SizeLimit { get; set; }")]
    [Row("internal int SizeLimit { get; set; }")]
    [Row("protected internal int SizeLimit { get; set; }")]
    public void AReachableProperty_IsStillAParameter(string property)
    {
        var result = Run(property).AssertNoErrors();

        Verify(result.SourceContaining("TestModule.Module").Contains("SizeLimit"));
        Verify(result.GeneratorDiagnostics.Any(d => d.Id == "DM0018"));
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
