using System.Reflection;
using DependencyModules.Runtime;
using DependencyModules.Runtime.Interfaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DependencyModules.Tests.Infrastructure;

/// <summary>
/// Compiles source with the generator, emits a real assembly, loads it, and builds a service
/// provider from the generated module.
///
/// This is the counterpart to asserting on generated text. The generator's contract is not that its
/// output contains particular strings, it is that the code the compiler produces registers the right
/// services with the right lifetimes and that they resolve. Tests built on this class exercise the
/// registrations the same way an application would, so a change that still emits plausible-looking
/// code but breaks behaviour fails here.
/// </summary>
public class GeneratedAssembly {
    private static int _assemblyCounter;

    private readonly Assembly _assembly;

    private GeneratedAssembly(Assembly assembly, IServiceCollection services) {
        _assembly = assembly;
        Services = services;
    }

    /// <summary>
    /// The populated collection, for asserting on registrations that cannot be observed by
    /// resolving, such as lifetime or registration method.
    /// </summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Compiles <paramref name="source"/>, loads the result, and applies the named module.
    /// </summary>
    /// <param name="source">Source to compile. Types are expected in the TestNamespace namespace.</param>
    /// <param name="moduleName">Name of the module type to apply, without its namespace.</param>
    /// <param name="buildProperties">MSBuild properties visible to the generator.</param>
    /// <param name="withConventions">Also runs the convention generator, which ships separately.</param>
    /// <param name="environment">
    /// The environment conditional registrations are evaluated against. Null takes the path an
    /// application takes when it calls AddModules without one, which is the process environment.
    /// </param>
    public static GeneratedAssembly Create(
        string source,
        string moduleName = "TestModule",
        IReadOnlyDictionary<string, string>? buildProperties = null,
                IModuleEnvironment? environment = null,
        IReadOnlyList<MetadataReference>? additionalReferences = null) {

        var assemblyName = "GeneratedAssemblyTest" + Interlocked.Increment(ref _assemblyCounter);

        var result = GeneratorTestHarness.Run(
            new Dictionary<string, string> { ["Test.cs"] = source },
            buildProperties,
            assemblyName: assemblyName,
                        additionalReferences: additionalReferences);

        result.AssertNoErrors();

        var assembly = Emit(result, assemblyName);

        var moduleType = assembly.GetType($"TestNamespace.{moduleName}")
                         ?? throw new InvalidOperationException(
                             $"The compiled assembly has no type 'TestNamespace.{moduleName}'. " +
                             $"Types present: {string.Join(", ", assembly.GetTypes().Select(t => t.FullName))}");

        if (Activator.CreateInstance(moduleType) is not IDependencyModule module) {
            throw new InvalidOperationException(
                $"'{moduleType.FullName}' did not implement IDependencyModule. The generator should " +
                "have added that to the partial declaration.");
        }

        var services = new ServiceCollection();

        // AddModules rather than AddModule so an environment can be supplied; both reach
        // DependencyRegistry.LoadModules, and AddModules is the call an application makes.
        services.AddModules(environment, module);

        return new GeneratedAssembly(assembly, services);
    }

    private static Assembly Emit(GeneratorResult result, string assemblyName) {
        using var stream = new MemoryStream();

        EmitResult emitResult = result.Compilation.Emit(stream);

        Assert.True(emitResult.Success,
            $"The generated code did not emit. Errors:{Environment.NewLine}" +
            string.Join(Environment.NewLine,
                emitResult.Diagnostics
                    .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                    .Select(diagnostic => $"  {diagnostic.Id} {diagnostic.GetMessage()}")));

        // Loaded into the default context so it binds against the DependencyModules.Runtime already
        // loaded by this test assembly. That is what lets tests cast to IDependencyModule and
        // resolve through the real extension methods rather than through reflection.
        return Assembly.Load(stream.ToArray());
    }

    /// <summary>
    /// A type from the compiled assembly, by name within TestNamespace.
    /// </summary>
    public Type Type(string name) =>
        _assembly.GetType($"TestNamespace.{name}")
        ?? throw new InvalidOperationException(
            $"No type 'TestNamespace.{name}'. Present: {string.Join(", ", _assembly.GetTypes().Select(t => t.Name))}");

    /// <summary>
    /// Builds a provider from the applied registrations.
    /// </summary>
    public ServiceProvider BuildProvider() => Services.BuildServiceProvider();

    /// <summary>
    /// Resolves a service by type name, failing the test if it was never registered.
    /// </summary>
    public object ResolveRequired(string typeName) {
        var provider = BuildProvider();
        var service = provider.GetService(Type(typeName));

        Assert.True(service != null,
            $"'{typeName}' did not resolve. Registered service types: " +
            string.Join(", ", Services.Select(descriptor => descriptor.ServiceType.Name)));

        return service!;
    }

    /// <summary>
    /// The descriptor registered for a service type, failing the test if there is not exactly one.
    /// </summary>
    public ServiceDescriptor Descriptor(string serviceTypeName) {
        var serviceType = Type(serviceTypeName);
        var matches = Services.Where(descriptor => descriptor.ServiceType == serviceType).ToArray();

        Assert.True(matches.Length == 1,
            $"Expected exactly one registration for '{serviceTypeName}', found {matches.Length}.");

        return matches[0];
    }

    public IReadOnlyList<ServiceDescriptor> Descriptors(string serviceTypeName) {
        var serviceType = Type(serviceTypeName);

        return Services.Where(descriptor => descriptor.ServiceType == serviceType).ToArray();
    }
}
