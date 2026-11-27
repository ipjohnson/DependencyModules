using DependencyModules.Tests.Infrastructure;

namespace DependencyModules.Specs.GeneratorTests;

/// <summary>
/// DM0021: a <c>[Mock]</c> parameter and a <c>[TestExport]</c> on the same method, both naming one
/// service.
///
/// A parameter attribute is the narrowest thing a test can say, so <c>[Mock]</c> wins and the
/// <c>[TestExport]</c> beside it does nothing. Written on the same method the two say opposite
/// things about one service in the same breath, and only one of them can have been meant — so it is
/// reported rather than resolved quietly, which is how the field report met it.
///
/// The scope is the whole point. A <c>[TestExport]</c> on the class or the assembly is a default for
/// everything under it, and one test overriding it for one argument is exactly what having both
/// scopes is for. Reporting that would be reporting the feature.
/// </summary>
public partial class MockTestExportDiagnosticTests
{
    [VerifyTest]
    public void BothOnOneMethod_ReportsDM0021()
    {
        var result = Run(
            """
            [TestExport(typeof(IThing), Implementation = typeof(RealThing))]
            public void Conflicting([Mock] IThing thing) { }
            """
        );

        var diagnostic = Single(result.GeneratorDiagnostics, d => d.Id == "DM0021");

        Verify(diagnostic.GetMessage().Contains("Conflicting"));
        Verify(diagnostic.GetMessage().Contains("IThing"));
    }

    /// <summary>
    /// Reported at the parameter, which is the half that wins and the half a reader has to look at
    /// to see what actually happens.
    /// </summary>
    [VerifyTest]
    public void ItIsReportedAtTheParameter()
    {
        var result = Run(
            """
            [TestExport(typeof(IThing), Implementation = typeof(RealThing))]
            public void Conflicting([Mock] IThing thing) { }
            """
        );

        var diagnostic = Single(result.GeneratorDiagnostics, d => d.Id == "DM0021");

        Verify(diagnostic.Location.SourceTree is not null);
        Verify(System.IO.Path.GetFileName(diagnostic.Location.GetLineSpan().Path) == "Test.cs");
    }

    /// <summary>
    /// The class-level default a test opts out of. This is the shape the override exists for.
    /// </summary>
    [VerifyTest]
    public void TestExportOnTheClass_IsNotReported()
    {
        var result = GeneratorTestHarness.Run(
            $$"""
            {{Preamble}}

            [TestExport(typeof(IThing), Implementation = typeof(RealThing))]
            public class Fixture {
                public void Overriding([Mock] IThing thing) { }
            }
            """
        );

        Verify(!result.GeneratorDiagnostics.Any(d => d.Id == "DM0021"));
    }

    [VerifyTest]
    public void TestExportOnTheAssembly_IsNotReported()
    {
        var result = GeneratorTestHarness.Run(
            $$"""
            {{Preamble}}

            [assembly: TestExport(typeof(IThing), Implementation = typeof(RealThing))]

            public class Fixture {
                public void Overriding([Mock] IThing thing) { }
            }
            """
        );

        Verify(!result.GeneratorDiagnostics.Any(d => d.Id == "DM0021"));
    }

    /// <summary>
    /// Naming different services is two unrelated declarations, not a disagreement.
    /// </summary>
    [VerifyTest]
    public void BothOnOneMethodNamingDifferentServices_IsNotReported()
    {
        var result = Run(
            """
            [TestExport(typeof(IOther), Implementation = typeof(RealOther))]
            public void Unrelated([Mock] IThing thing) { }
            """
        );

        Verify(!result.GeneratorDiagnostics.Any(d => d.Id == "DM0021"));
    }

    [VerifyTest]
    public void AMockWithNoTestExport_IsNotReported()
    {
        var result = Run("public void JustAMock([Mock] IThing thing) { }");

        Verify(!result.GeneratorDiagnostics.Any(d => d.Id == "DM0021"));
    }

    [VerifyTest]
    public void ATestExportWithNoMock_IsNotReported()
    {
        var result = Run(
            """
            [TestExport(typeof(IThing), Implementation = typeof(RealThing))]
            public void JustAnExport(IThing thing) { }
            """
        );

        Verify(!result.GeneratorDiagnostics.Any(d => d.Id == "DM0021"));
    }

    /// <summary>
    /// A keyed <c>[Mock]</c> is registered under its key. The unkeyed export stays in use.
    /// </summary>
    [VerifyTest]
    public void AKeyedMock_IsNotReported()
    {
        var result = Run(
            """
            [TestExport(typeof(IThing), Implementation = typeof(RealThing))]
            public void Keyed([Mock] [Microsoft.Extensions.DependencyInjection.FromKeyedServices("k")] IThing thing) { }
            """
        );

        Verify(!result.GeneratorDiagnostics.Any(d => d.Id == "DM0021"));
    }

    /// <summary>
    /// Moq registers <c>T</c> itself when the test also takes <c>Mock&lt;T&gt;</c>, and
    /// <c>[Mock] T</c> then registers nothing. The export replaces <c>mock.Object</c>, so the
    /// parameter gets the export.
    /// </summary>
    [VerifyTest]
    public void AMockPairedWithAMoqMock_IsNotReported()
    {
        var result = Run(
            """
            [TestExport(typeof(IThing), Implementation = typeof(RealThing))]
            public void Paired([Mock] IThing thing, Moq.Mock<IThing> mock) { }
            """
        );

        Verify(!result.GeneratorDiagnostics.Any(d => d.Id == "DM0021"));
    }

    [VerifyTest]
    public void AMoqMockOfAnotherService_IsStillReported()
    {
        var result = Run(
            """
            [TestExport(typeof(IThing), Implementation = typeof(RealThing))]
            public void Unpaired([Mock] IThing thing, Moq.Mock<IOther> other) { }
            """
        );

        Single(result.GeneratorDiagnostics, d => d.Id == "DM0021");
    }

    /// <summary>
    /// Resolved rather than string-matched, the way every other attribute this generator reads is.
    /// </summary>
    [VerifyTest]
    public void QualifiedSpellings_AreStillReported()
    {
        var result = Run(
            """
            [DependencyModules.Testing.Attributes.TestExport(typeof(IThing), Implementation = typeof(RealThing))]
            public void Conflicting([global::DependencyModules.Testing.Attributes.Mock] IThing thing) { }
            """
        );

        Single(result.GeneratorDiagnostics, d => d.Id == "DM0021");
    }

    /// <summary>
    /// VerificationGlue's <c>[Mock]</c> wins over <c>[TestExport]</c> as DependencyModules' does, so
    /// the two spellings get the same report at the same place.
    /// </summary>
    [VerifyTest]
    public void VerificationGlueMock_GetsTheSameReportAsDependencyModulesMock()
    {
        const string body = """
            [TestExport(typeof(IThing), Implementation = typeof(RealThing))]
            public void Conflicting([Mock] IThing thing) { }
            """;

        var dependencyModules = Single(Run(body).GeneratorDiagnostics, d => d.Id == "DM0021");
        var verificationGlueResult = RunVerificationGlue(body);
        var verificationGlue = Single(
            verificationGlueResult.GeneratorDiagnostics,
            d => d.Id == "DM0021"
        );

        Verify(!verificationGlueResult.Errors.Any());
        var reportedText = verificationGlue
            .Location.SourceTree!.GetText()
            .ToString(verificationGlue.Location.SourceSpan);

        Verify(dependencyModules.GetMessage() == verificationGlue.GetMessage());
        Verify(dependencyModules.Severity == verificationGlue.Severity);
        Verify(
            System.IO.Path.GetFileName(verificationGlue.Location.GetLineSpan().Path) == "Test.cs"
        );
        Verify(reportedText == "[Mock] IThing thing");
    }

    [VerifyTest]
    public void VerificationGlueMock_QualifiedSpelling_IsReported()
    {
        var result = RunVerificationGlue(
            """
            [TestExport(typeof(IThing), Implementation = typeof(RealThing))]
            public void Conflicting([global::VerificationGlue.Mocks.Mock] IThing thing) { }
            """
        );

        Verify(!result.Errors.Any());
        Single(result.GeneratorDiagnostics, d => d.Id == "DM0021");
    }

    [VerifyTest]
    public void VerificationGlueMock_Keyed_IsNotReported()
    {
        var result = RunVerificationGlue(
            """
            [TestExport(typeof(IThing), Implementation = typeof(RealThing))]
            public void Keyed([Mock] [Microsoft.Extensions.DependencyInjection.FromKeyedServices("k")] IThing thing) { }
            """
        );

        Verify(!result.Errors.Any());
        Verify(!result.GeneratorDiagnostics.Any(d => d.Id == "DM0021"));
    }

    [VerifyTest]
    public void VerificationGlueMock_NamingAnotherService_IsNotReported()
    {
        var result = RunVerificationGlue(
            """
            [TestExport(typeof(IOther), Implementation = typeof(RealOther))]
            public void Unrelated([Mock] IThing thing) { }
            """
        );

        Verify(!result.Errors.Any());
        Verify(!result.GeneratorDiagnostics.Any(d => d.Id == "DM0021"));
    }

    [VerifyTest]
    public void VerificationGlueMock_WithTestExportOnTheClass_IsNotReported()
    {
        var result = GeneratorTestHarness.Run(
            $$"""
            {{VerificationGluePreamble}}

            namespace TestNamespace
            {
                [TestExport(typeof(IThing), Implementation = typeof(RealThing))]
                public class Fixture {
                    public void Overriding([Mock] IThing thing) { }
                }
            }
            """
        );

        Verify(!result.Errors.Any());
        Verify(!result.GeneratorDiagnostics.Any(d => d.Id == "DM0021"));
    }

    /// <summary>
    /// Matched by namespace and name, so a <c>MockAttribute</c> of another library is not read.
    /// </summary>
    [VerifyTest]
    public void AMockAttributeInAnotherNamespace_IsNotReported()
    {
        var result = GeneratorTestHarness.Run(
            $$"""
            using DependencyModules.Testing.Attributes;

            namespace Elsewhere
            {
                [System.AttributeUsage(System.AttributeTargets.Parameter)]
                public sealed class MockAttribute : System.Attribute;
            }

            namespace TestNamespace
            {
                public interface IThing;

                public class RealThing : IThing;

                public class Fixture {
                    [TestExport(typeof(IThing), Implementation = typeof(RealThing))]
                    public void NotAMock([Elsewhere.Mock] IThing thing) { }
                }
            }
            """
        );

        Verify(!result.Errors.Any());
        Verify(!result.GeneratorDiagnostics.Any(d => d.Id == "DM0021"));
    }

    /// <summary>
    /// A VerificationGlue test file. The stub stands in for <c>VerificationGlue.Mocks</c>, which no
    /// project here references. <c>[TestExport]</c> is imported by alias, because importing both
    /// namespaces makes <c>[Mock]</c> ambiguous.
    /// </summary>
    private const string VerificationGluePreamble = """
        using VerificationGlue.Mocks;
        using TestExportAttribute = DependencyModules.Testing.Attributes.TestExportAttribute;

        namespace VerificationGlue.Mocks
        {
            [System.AttributeUsage(System.AttributeTargets.Parameter)]
            public sealed class MockAttribute : System.Attribute;
        }

        namespace TestNamespace
        {
            public interface IThing;

            public interface IOther;

            public class RealThing : IThing;

            public class RealOther : IOther;
        }
        """;

    private static GeneratorResult RunVerificationGlue(string body) =>
        GeneratorTestHarness.Run(
            $$"""
            {{VerificationGluePreamble}}

            namespace TestNamespace
            {
                public class Fixture {
                    {{body}}
                }
            }
            """
        );

    private const string Preamble = """
        using DependencyModules.Runtime.Attributes;
        using DependencyModules.Testing.Attributes;

        namespace TestNamespace;

        public interface IThing;

        public interface IOther;

        public class RealThing : IThing;

        public class RealOther : IOther;
        """;

    private static GeneratorResult Run(string body) =>
        GeneratorTestHarness.Run(
            $$"""
            {{Preamble}}

            public class Fixture {
                {{body}}
            }
            """
        );
}
