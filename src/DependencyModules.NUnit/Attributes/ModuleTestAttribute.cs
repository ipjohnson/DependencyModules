using System.Globalization;
using System.Reflection;
using DependencyModules.NUnit.Impl;
using DependencyModules.Testing.Attributes.Interfaces;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
using NUnit.Framework.Internal.Builders;
using NUnit.Framework.Internal.Commands;

namespace DependencyModules.NUnit.Attributes;

/// <summary>
/// Marks a method as a module test: a container is built from the named modules for every iteration
/// of the test, the method's parameters are resolved from it, and it is torn down when the iteration
/// ends.
/// </summary>
/// <remarks>
/// The container's lifetime brackets the whole iteration — <c>[SetUp]</c>, the test method, then
/// <c>[TearDown]</c> — because this wraps through <see cref="IWrapSetUpTearDown"/> rather than
/// <see cref="IWrapTestMethod"/>. Wrapping the test method alone would put the container inside
/// setup and teardown, leaving <c>[SetUp]</c> running before it exists and <c>[TearDown]</c> after
/// it is disposed, so neither could touch a service.
///
/// <c>[Repeat]</c> and <c>[Retry]</c> wrap outside both, so each repetition and each retry attempt
/// builds and tears down its own container rather than sharing one.
///
/// <c>[TestFixture]</c> on the containing class is optional; this implies a fixture the way
/// <c>[Test]</c> does.
/// </remarks>
/// <example>
/// <code>
/// [ModuleTest(typeof(MyModule))]
/// public void ResolvesTheService(IMyService service)
/// {
///     Assert.That(service, Is.Not.Null);
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method)]
public class ModuleTestAttribute
    : Attribute,
        ITestBuilder,
        IWrapSetUpTearDown,
        IImplyFixture,
        IModuleTestAttribute
{
    /// <summary>
    /// Where a row's arguments are stashed between building the test case and executing it.
    /// </summary>
    /// <remarks>
    /// Not passed through <c>TestCaseParameters.Arguments</c>, which by then also holds the
    /// placeholders standing in for the parameters the container will supply. This keeps the row
    /// itself, so execution knows how many leading arguments are real.
    ///
    /// Set on every test this builds, as an empty row when the method has none. A test without it
    /// was built by NUnit from one of its own data attributes.
    /// </remarks>
    internal const string RowPropertyName = "DependencyModules.ModuleTestRow";

    // NUnit creates the attribute while it builds the tests, before the first one runs.
    static ModuleTestAttribute() => NUnitCurrentTestProvider.Install();

    /// <summary>
    /// Marks a test method, optionally naming the modules to configure its container with.
    /// </summary>
    /// <remarks>
    /// One constructor covers every arity, where the xUnit attribute needs three. That attribute
    /// derives from <c>FactAttribute</c>, which captures a source location through
    /// <c>[CallerFilePath]</c> and <c>[CallerLineNumber]</c>, and C# does not allow caller-info
    /// parameters after a params array. NUnit takes navigation from the assembly's symbols instead,
    /// so nothing is lost by taking the params form here.
    /// </remarks>
    public ModuleTestAttribute(params Type[] modules)
    {
        ModuleTypes = modules;
    }

    /// <inheritdoc />
    public Type[] ModuleTypes { get; }

    /// <summary>
    /// Builds one test case per data row, or a single case when the method has no rows.
    /// </summary>
    /// <remarks>
    /// Nothing is resolved here. NUnit calls this during discovery, and building a container per
    /// test at discovery would construct every mock in the assembly before the first test ran. All
    /// this has to satisfy is NUnit's arity check, which an array of the right length does; the real
    /// arguments are written into that array at execution time, once there is a container.
    /// </remarks>
    public IEnumerable<TestMethod> BuildFrom(IMethodInfo method, Test? suite)
    {
        var parameterCount = method.GetParameters().Length;

        // Each row keeps the name of the attribute it came from. Rows from other data attributes
        // have no name, so a name matched to a row by its position can go to the wrong row.
        var rows = method
            .MethodInfo.GetCustomAttributes(false)
            .OfType<IModuleTestDataAttribute>()
            .SelectMany(dataAttribute =>
                dataAttribute
                    .GetRows(method.MethodInfo)
                    .Select(row =>
                        (Row: row, Name: (dataAttribute as ModuleTestCaseAttribute)?.TestName)
                    )
            )
            .ToArray();

        if (rows.Length == 0)
        {
            var single = BuildTestMethod(
                method,
                suite,
                new object?[parameterCount],
                [],
                method.Name
            );

            // Without a row, every parameter comes from the container, and a parameter meant for
            // the data attribute of NUnit is not in it.
            if (NUnitDataMessage(method.MethodInfo) is { } message)
            {
                single.RunState = RunState.NotRunnable;
                single.Properties.Set(PropertyNames.SkipReason, message);
            }

            yield return single;

            yield break;
        }

        foreach (var (row, name) in rows)
        {
            var arguments = new object?[parameterCount];

            if (row.Length <= parameterCount)
            {
                Array.Copy(row, arguments, row.Length);
            }

            var testName = name ?? DisplayName(method.Name, row);

            var testMethod = BuildTestMethod(method, suite, arguments, row, testName);

            if (row.Length > parameterCount)
            {
                // Reported as a failing test rather than thrown, so one bad row names itself instead
                // of taking down discovery for the whole fixture.
                testMethod.RunState = RunState.NotRunnable;
                testMethod.Properties.Set(
                    PropertyNames.SkipReason,
                    $"[ModuleTestCase] supplied {row.Length} arguments to a method taking "
                        + $"{parameterCount}. A row may supply fewer than the method takes — the remaining "
                        + "parameters are resolved from the container — but not more."
                );
            }

            yield return testMethod;
        }
    }

    /// <inheritdoc />
    public TestCommand Wrap(TestCommand command) => new ModuleTestCommand(command);

    /// <summary>
    /// Why the data attributes of NUnit on <paramref name="method"/> do not work with
    /// <c>[ModuleTest]</c>, or null when it has none.
    /// </summary>
    /// <remarks>
    /// NUnit builds tests of its own from them, and <see cref="Wrap"/> puts the container around
    /// those tests too. They carry no row, so the values of the attribute never reach the method.
    /// </remarks>
    internal static string? NUnitDataMessage(MethodInfo method)
    {
        var parameterAttributes = method
            .GetParameters()
            .SelectMany(parameter => parameter.GetCustomAttributes(false))
            .Where(attribute => attribute is IParameterDataSource);

        var names = method
            .GetCustomAttributes(false)
            .Where(attribute => attribute is ITestBuilder and not ModuleTestAttribute)
            .Concat(parameterAttributes)
            .Select(attribute => "[" + TrimAttributeSuffix(attribute.GetType().Name) + "]")
            .Distinct()
            .ToArray();

        if (names.Length == 0)
        {
            return null;
        }

        return $"NUnit builds tests of its own for '{method.Name}' from {string.Join(" and ", names)}, "
            + "and [ModuleTest] cannot give their values to the method. Use [ModuleTestCase] for the "
            + "rows of a module test, or an attribute that implements IModuleTestDataAttribute.";
    }

    private static string TrimAttributeSuffix(string name) =>
        name.EndsWith("Attribute", StringComparison.Ordinal)
            ? name.Substring(0, name.Length - "Attribute".Length)
            : name;

    private static TestMethod BuildTestMethod(
        IMethodInfo method,
        Test? suite,
        object?[] arguments,
        object?[] row,
        string testName
    )
    {
        var parameters = new TestCaseParameters(arguments) { TestName = testName };

        var testMethod = new NUnitTestCaseBuilder().BuildTestMethod(method, suite, parameters);

        testMethod.Properties.Set(RowPropertyName, row);

        return testMethod;
    }

    /// <summary>
    /// Names a row after its own arguments, the way NUnit names a <c>[TestCase]</c>.
    /// </summary>
    /// <remarks>
    /// Only the row's arguments are used. The trailing placeholders are resolved from the container
    /// at execution time, and a name built from them would read as a list of nulls.
    /// </remarks>
    private static string DisplayName(string methodName, object?[] row) =>
        $"{methodName}({string.Join(", ", row.Select(FormatArgument))})";

    /// <remarks>
    /// NUnit's own <c>MsgUtils.FormatValue</c> is not part of its public surface, so this quotes the
    /// two cases that need it and leaves everything else to <c>ToString</c>. The invariant culture
    /// keeps a name that a test explorer filters on from changing with the machine's locale.
    /// </remarks>
    private static string FormatArgument(object? argument) =>
        argument switch
        {
            null => "null",
            string text => $"\"{text}\"",
            char character => $"'{character}'",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => argument.ToString() ?? string.Empty,
        };
}
