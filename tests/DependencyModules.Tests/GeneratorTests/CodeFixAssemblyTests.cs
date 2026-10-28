using System.Reflection;
using System.Runtime.Loader;
using DependencyModules.CodeFixes;
using Microsoft.CodeAnalysis.CodeFixes;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// The code fix assembly loads the way an IDE loads it from analyzers/dotnet/cs: Roslyn and the
/// framework come from the host, and nothing else is there to resolve.
/// </summary>
public class CodeFixAssemblyTests
{
    [Fact]
    public void TheCodeFixAssembly_LoadsWithOnlyHostAssemblies()
    {
        var context = new HostOnlyLoadContext();

        try
        {
            var assembly = context.LoadFromAssemblyPath(
                typeof(ServiceTypeCodeFixProvider).Assembly.Location
            );

            var providers = assembly
                .GetTypes()
                .Where(type => type.GetCustomAttribute<ExportCodeFixProviderAttribute>() != null)
                .Select(type => (CodeFixProvider)Activator.CreateInstance(type)!)
                .ToArray();

            var provider = Assert.Single(providers);
            Assert.Equal(new[] { "DM0026" }, provider.FixableDiagnosticIds);
            Assert.NotNull(provider.GetFixAllProvider());
            Assert.Empty(context.Refused);
        }
        finally
        {
            context.Unload();
        }
    }

    /// <summary>
    /// Takes Roslyn, System.Composition and the framework from the test host, and refuses the rest,
    /// such as the generator assembly that sits beside this one in the package.
    /// </summary>
    private sealed class HostOnlyLoadContext() : AssemblyLoadContext(isCollectible: true)
    {
        public List<string> Refused { get; } = new();

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var name = assemblyName.Name ?? "";

            if (
                name is "netstandard" or "mscorlib"
                || name.StartsWith("System.", StringComparison.Ordinal)
                || name.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal)
            )
            {
                return Default.LoadFromAssemblyName(assemblyName);
            }

            Refused.Add(name);

            throw new FileNotFoundException($"'{name}' is not supplied by the host.", name);
        }
    }
}
