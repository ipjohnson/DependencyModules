using System.Reflection;
using DependencyModules.NUnit.Attributes;
using DependencyModules.Runtime.Interfaces;
using DependencyModules.Testing.Attributes.Interfaces;
using DependencyModules.Testing.Impl;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework.Interfaces;
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
public class ModuleTestCommand(TestCommand innerCommand) : DelegatingTestCommand(innerCommand)
{
    /// <inheritdoc />
    public override TestResult Execute(TestExecutionContext context)
    {
        var testMethod = (TestMethod)Test;
        var method = testMethod.Method!.MethodInfo;

        // NUnit built this test from one of its own data attributes. It has no row, so no container
        // is built for it.
        if (!testMethod.Properties.ContainsKey(ModuleTestAttribute.RowPropertyName))
        {
            context.CurrentResult.SetResult(
                ResultState.NotRunnable,
                ModuleTestAttribute.NUnitDataMessage(method)
                    ?? $"NUnit built this test of '{method.Name}', so [ModuleTest] cannot give it arguments."
            );

            return context.CurrentResult;
        }

        // Widest scope first: assembly, then declaring type, then the method.
        var knownAttributes = method.GetTestAttributes<Attribute>().ToArray();

        var moduleContext = new NUnitTestMethodContext(testMethod, knownAttributes);

        var serviceCollection = new ServiceCollection();

        SetupTestCaseInfo(serviceCollection, testMethod, knownAttributes);

        // NUnit's command chain is synchronous. TestCommand.Execute has no async form.
        var container = ModuleTestComposition
            .BuildAsync(
                services: serviceCollection,
                modules: CollectModules(method, knownAttributes),
                testMethod: moduleContext,
                overrides: [],
                currentTest: null,
                parent: null
            )
            .AsTask()
            .GetAwaiter()
            .GetResult();

        try
        {
            var arguments = container
                .ResolveArgumentsAsync(RowArguments(testMethod))
                .GetAwaiter()
                .GetResult();

            PublishArguments(testMethod, container.Services, arguments);

            return innerCommand.Execute(context);
        }
        finally
        {
            container.DisposeAsync().AsTask().GetAwaiter().GetResult();
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
        TestMethod testMethod,
        IServiceProvider serviceProvider,
        object?[] arguments
    )
    {
        var target = testMethod.Arguments;

        Array.Copy(arguments, target, arguments.Length);

        serviceProvider.GetRequiredService<TestCaseInfo>().TestMethodArguments = arguments;
    }

    private static void SetupTestCaseInfo(
        IServiceCollection serviceCollection,
        TestMethod testMethod,
        Attribute[] knownAttributes
    )
    {
        serviceCollection.AddSingleton<ITestCaseInfo>(provider =>
            provider.GetRequiredService<TestCaseInfo>()
        );
        serviceCollection.AddSingleton(_ => new TestCaseInfo(
            testMethod,
            ArraySegment<object?>.Empty,
            knownAttributes
        ));
    }

    /// <summary>
    /// The modules in declaration order: those of <c>[ModuleTest]</c> first, then the module
    /// attributes, widest scope first. <see cref="ModuleTestComposition.BuildAsync"/> reverses them.
    /// </summary>
    private static IReadOnlyList<IDependencyModule> CollectModules(
        MethodInfo method,
        Attribute[] knownAttributes
    )
    {
        var modules = new List<IDependencyModule>();

        var testAttribute = method.GetTestAttribute<IModuleTestAttribute>();

        if (testAttribute != null)
        {
            foreach (var moduleType in testAttribute.ModuleTypes)
            {
                if (Activator.CreateInstance(moduleType, []) is IDependencyModule moduleInstance)
                {
                    modules.Add(moduleInstance);
                }
            }
        }

        foreach (var loadModuleAttribute in knownAttributes.OfType<IDependencyModuleProvider>())
        {
            modules.Add(loadModuleAttribute.GetModule());
        }

        return modules;
    }
}
