using System.Reflection;
using DependencyModules.NUnit.Attributes;
using DependencyModules.Runtime.Helpers;
using DependencyModules.Runtime.Interfaces;
using DependencyModules.Testing.Attributes.Interfaces;
using DependencyModules.Testing.Impl;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework.Internal;
using NUnit.Framework.Internal.Commands;

namespace DependencyModules.NUnit.Impl;

/// <summary>
/// Builds a test's container, resolves its arguments, and disposes the container when the test
/// iteration ends.
/// </summary>
/// <remarks>
/// Wrapped around NUnit's setup/teardown chain rather than around the method invocation, so the
/// container outlives <c>[SetUp]</c> and <c>[TearDown]</c> rather than being created between them.
///
/// The method is not invoked here. NUnit's own command does that, which is what keeps setup,
/// teardown, timeouts, expected exceptions and the rest working normally — this only has to make
/// sure the arguments are in place before delegating.
/// </remarks>
public class ModuleTestCommand(TestCommand innerCommand) : DelegatingTestCommand(innerCommand) {

    /// <inheritdoc />
    public override TestResult Execute(TestExecutionContext context) {
        var testMethod = (TestMethod)Test;
        var method = testMethod.Method!.MethodInfo;

        // Widest scope first: assembly, then declaring type, then the method.
        var knownAttributes = method.GetTestAttributes<Attribute>().ToArray();

        var moduleContext = new NUnitTestMethodContext(testMethod, knownAttributes);

        var serviceCollection = new ServiceCollection();

        // One resolver per container. A repeated test builds both again for every iteration.
        var resolver = new TestParameterResolver(moduleContext);

        SetupTestCaseInfo(serviceCollection, testMethod, knownAttributes);

        SetupModules(serviceCollection, method, knownAttributes);

        SetupServiceSetupAttributes(moduleContext, serviceCollection, knownAttributes);

        // Last, for the reason the xUnit host records at the same point: a [Mock] on a parameter
        // beats a [TestExport] naming the same service.
        resolver.SetupServiceCollection(serviceCollection);

        var serviceProvider = BuildServiceProvider(moduleContext, serviceCollection, knownAttributes);

        try {
            foreach (var startupAttribute in knownAttributes.OfType<ITestStartupAttribute>()) {
                // NUnit's command chain is synchronous — TestCommand.Execute has no async form — so
                // an async hook is awaited here rather than up the stack.
                startupAttribute.StartupAsync(moduleContext, serviceProvider).GetAwaiter().GetResult();
            }

            var arguments = resolver
                .ResolveArgumentsAsync(serviceProvider, RowArguments(testMethod))
                .GetAwaiter().GetResult();

            PublishArguments(testMethod, serviceProvider, arguments);

            return innerCommand.Execute(context);
        } finally {
            DisposeProvider(serviceProvider);
        }
    }

    /// <summary>
    /// The arguments a data row fixed for this case, or none.
    /// </summary>
    private static object?[] RowArguments(TestMethod testMethod) =>
        testMethod.Properties.Get(ModuleTestAttribute.RowPropertyName) as object?[] ?? [];

    /// <summary>
    /// Hands the resolved arguments to the command that will invoke the method.
    /// </summary>
    /// <remarks>
    /// NUnit invokes with the same <c>object?[]</c> instance that was handed to
    /// <c>TestCaseParameters</c> when the case was built, so filling that array in is what makes an
    /// argument that did not exist at build time reach the method. There is no setter to assign a
    /// new array through, and taking over the invocation to pass one would mean reimplementing
    /// NUnit's setup and teardown handling.
    /// </remarks>
    private static void PublishArguments(
        TestMethod testMethod, IServiceProvider serviceProvider, object?[] arguments) {
        var target = testMethod.Arguments;

        Array.Copy(arguments, target, arguments.Length);

        serviceProvider.GetRequiredService<TestCaseInfo>().TestMethodArguments = arguments;
    }

    private static void SetupTestCaseInfo(
        IServiceCollection serviceCollection, TestMethod testMethod, Attribute[] knownAttributes) {
        serviceCollection.AddSingleton<ITestCaseInfo>(provider => provider.GetRequiredService<TestCaseInfo>());
        serviceCollection.AddSingleton(_ => new TestCaseInfo(
            testMethod,
            ArraySegment<object?>.Empty,
            knownAttributes));
    }

    /// <remarks>
    /// Last rather than first: <paramref name="knownAttributes"/> is widest scope first — assembly,
    /// then declaring type, then the method — so the last one is the narrowest, and a builder on the
    /// method beats one on the class beats one on the assembly. Taking the first would have let an
    /// assembly-level builder silently win over the method that asked for a different container,
    /// which is the reverse of how every other attribute here resolves.
    /// </remarks>
    private static IServiceProvider BuildServiceProvider(
        ITestMethodContext context, IServiceCollection serviceCollection, Attribute[] knownAttributes) {
        var serviceProviderBuilderAttribute =
            knownAttributes.OfType<IServiceProviderBuilderAttribute>().LastOrDefault();

        if (serviceProviderBuilderAttribute != null) {
            return serviceProviderBuilderAttribute.BuildServiceProvider(context, serviceCollection);
        }

        return serviceCollection.BuildServiceProvider();
    }

    /// <remarks>
    /// The whole pass runs after the parameter value providers, so a <c>[TestExport]</c> overrides a
    /// <c>[Mock]</c> of the same service rather than the other way round.
    ///
    /// Mock support goes first within the pass, everything else keeping its declared order behind it.
    /// A mock is the stand-in a test falls back to, so naming a real implementation has to beat it —
    /// and has to beat it whether <c>[MoqSupport]</c> sits on the assembly, the class or the method,
    /// which relying on attribute order alone would not guarantee.
    /// </remarks>
    private static void SetupServiceSetupAttributes(
        ITestMethodContext context, IServiceCollection serviceCollection, Attribute[] knownAttributes) {
        var setupAttributes = knownAttributes
            .OfType<ITestServiceSetupAttribute>()
            .OrderBy(attribute => attribute is IMockSupportAttribute ? 0 : 1);

        foreach (var setupAttribute in setupAttributes) {
            setupAttribute.SetupServiceCollection(context, serviceCollection);
        }
    }

    /// <remarks>
    /// The same loading the xUnit integration does, reading <see cref="IModuleTestAttribute"/> so
    /// neither names the other's attribute. It is not shared code because it needs both
    /// <c>DependencyModules.Runtime</c> and <c>DependencyModules.Testing</c>, and the only assembly
    /// both integrations share is Testing — which the three mocking packages reference precisely
    /// because it does not drag the runtime in behind it.
    /// </remarks>
    private static void SetupModules(
        IServiceCollection serviceCollection, MethodInfo method, IEnumerable<Attribute> knownAttributes) {
        var modules = new List<IDependencyModule>();

        foreach (var loadModuleAttribute in knownAttributes.OfType<IDependencyModuleProvider>()) {
            modules.Add(loadModuleAttribute.GetModule());
        }

        var testAttribute = method.GetTestAttribute<IModuleTestAttribute>();

        if (testAttribute != null) {
            var count = 0;
            foreach (var moduleType in testAttribute.ModuleTypes) {
                if (Activator.CreateInstance(moduleType, []) is IDependencyModule moduleInstance) {
                    modules.Insert(count++, moduleInstance);
                }
            }
        }

        modules.Reverse();

        DependencyRegistry<object>.LoadModules(serviceCollection, modules.ToArray());
    }

    /// <remarks>
    /// Asynchronous disposal is preferred where the provider offers it, because a service that only
    /// implements <see cref="IAsyncDisposable"/> makes <c>ServiceProvider.Dispose</c> throw rather
    /// than fall back.
    /// </remarks>
    private static void DisposeProvider(IServiceProvider serviceProvider) {
        switch (serviceProvider) {
            case IAsyncDisposable asyncDisposable:
                asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
                break;
            case IDisposable disposable:
                disposable.Dispose();
                break;
        }
    }
}
