using System.Reflection;
using DependencyModules.Runtime.Attributes;
using DependencyModules.Tests.Infrastructure;
using DependencyModules.xUnit.Attributes;
using PublicApiGenerator;
using Xunit;

namespace DependencyModules.Tests.ApiTests;

/// <summary>
/// Pins the public surface of every package that ships a referenceable assembly.
///
/// After 1.0.0 the public API is a compatibility promise. Nothing else in the suite notices if a
/// member is removed, a signature changes, or a type's accessibility narrows — the library still
/// builds, and its own tests still pass, because they are compiled together. These snapshots turn
/// any such change into a reviewable diff.
///
/// A failure here is not automatically a bug. It means the surface moved, and someone has to decide
/// whether that is intended and whether it warrants a major version. To accept a change:
///     UPDATE_SNAPSHOTS=1 dotnet test tests/DependencyModules.Tests
/// then read the diff carefully before committing it.
/// </summary>
public class PublicApiTests
{
    [Fact]
    public void RuntimeApi()
    {
        Snapshot.Match(ApiOf(typeof(DependencyModuleAttribute)));
    }

#if XUNIT_V4
    /// <summary>
    /// DependencyModules.xUnit4, which is the xUnit package's sources built against xunit.v3 4.x.
    /// The two surfaces differ where the xUnit types they expose differ.
    /// </summary>
    [Fact]
    public void XUnit4Api()
    {
        Snapshot.Match(ApiOf(typeof(ModuleTestAttribute)));
    }
#else
    [Fact]
    public void XUnitApi()
    {
        Snapshot.Match(ApiOf(typeof(ModuleTestAttribute)));
    }
#endif

    /// <summary>
    /// The NUnit integration. Its <c>[ModuleTest]</c> shares a name with xUnit's and nothing else —
    /// the two attributes derive from what their own framework requires, and only
    /// <c>IModuleTestAttribute</c> is common to both.
    /// </summary>
    [Fact]
    public void NUnitApi()
    {
        Snapshot.Match(
            ApiOf(typeof(global::DependencyModules.NUnit.Attributes.ModuleTestAttribute))
        );
    }

    /// <summary>
    /// The seam every mocking package implements, and the only assembly they share. It carries no
    /// test framework dependency, which is the point of it — a change here reaches all of them.
    /// </summary>
    [Fact]
    public void TestingApi()
    {
        Snapshot.Match(ApiOf(typeof(Testing.Attributes.Interfaces.IMockSupportAttribute)));
    }

#if NET10_0_OR_GREATER
    /// <summary>
    /// The VerificationGlue.Tests integration, which targets net10.0 only because
    /// VerificationGlue.Tests does.
    /// </summary>
    /// <remarks>
    /// Read from its build output rather than referenced. A reference would bring the
    /// Microsoft.Testing.Platform 3 preview into this project, and its MSBuild targets stop the
    /// VSTest run. The generator reads the file and does not run its code.
    /// </remarks>
    [Fact]
    public void VerificationGlueApi()
    {
        var path = typeof(PublicApiTests)
            .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "VerificationGlueAssembly")
            .Value!;

        Snapshot.Match(ApiOf(Assembly.LoadFile(Path.GetFullPath(path))));
    }
#endif

    [Fact]
    public void NSubstituteApi()
    {
        Snapshot.Match(
            ApiOf(typeof(global::DependencyModules.NSubstitute.NSubstituteSupportAttribute))
        );
    }

    [Fact]
    public void MoqApi()
    {
        Snapshot.Match(ApiOf(typeof(global::DependencyModules.Moq.MoqSupportAttribute)));
    }

    [Fact]
    public void FakeItEasyApi()
    {
        Snapshot.Match(
            ApiOf(typeof(global::DependencyModules.FakeItEasy.FakeItEasySupportAttribute))
        );
    }

    /// <summary>
    /// The generator ships as an analyzer rather than a reference assembly, so consumers never bind
    /// against its types. Its surface still matters, because the source-only
    /// DependencyModules.SourceGenerator.Impl package invites developers to build their own
    /// generators on top of these base classes.
    /// </summary>
    [Fact]
    public void SourceGeneratorApi()
    {
        Snapshot.Match(ApiOf(typeof(SourceGenerator.SourceGenerator)));
    }

    private static string ApiOf(Type typeFromAssembly) => ApiOf(typeFromAssembly.Assembly);

    private static string ApiOf(Assembly assembly) =>
        assembly.GeneratePublicApi(
            new ApiGeneratorOptions
            {
                // Assembly-level attributes are build metadata, not API, and several of them
                // (SourceLink, InternalsVisibleTo, TFM) change with build configuration.
                ExcludeAttributes =
                [
                    "System.Runtime.Versioning.TargetFrameworkAttribute",
                    "System.Reflection.AssemblyMetadataAttribute",
                    "System.Runtime.CompilerServices.InternalsVisibleToAttribute",
                    "System.Diagnostics.DebuggableAttribute",
                    "System.Runtime.CompilerServices.CompilationRelaxationsAttribute",
                ],
            }
        );
}
