using System.Reflection;

namespace DependencyModules.NUnit.Attributes;

/// <summary>
/// Supplies rows of arguments to a <c>[ModuleTest]</c> method.
/// </summary>
/// <remarks>
/// Implemented by data attributes, not by test authors. <c>[ModuleTest]</c> builds one test case per
/// row returned here, so a source of rows — a member, a file, a generator — only has to implement
/// this to become usable.
/// </remarks>
public interface IModuleTestDataAttribute {

    /// <summary>
    /// The rows to build test cases from. A row covers the leading parameters of the method; the
    /// rest are resolved from the test's container.
    /// </summary>
    /// <param name="method">The test method the rows are being built for.</param>
    IEnumerable<object?[]> GetRows(MethodInfo method);
}

/// <summary>
/// One row of arguments for a <c>[ModuleTest]</c> method, the equivalent of NUnit's
/// <c>[TestCase]</c> or xUnit's <c>[InlineData]</c>.
/// </summary>
/// <remarks>
/// NUnit's own <c>[TestCase]</c> cannot be used for this. It checks at build time that the row
/// supplies an argument for every parameter — throwing <c>TargetParameterCountException</c> before
/// any of this package's code runs — so it cannot express "the row covers the first parameters and
/// the container covers the rest", which is the whole point of a module test with data. It also
/// builds its own test cases, so combining the two would produce a case per row plus one more.
///
/// The arguments fill the leading parameters in order. Every parameter after them is resolved the
/// way an undecorated <c>[ModuleTest]</c> parameter is: from an attribute on it, then the container,
/// then direct construction.
/// </remarks>
/// <example>
/// <code>
/// [ModuleTest(typeof(MyModule))]
/// [ModuleTestCase(1, "one")]
/// [ModuleTestCase(2, "two")]
/// public void Converts(int number, string word, INumberFormatter formatter) {
///     Assert.That(formatter.Spell(number), Is.EqualTo(word));
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public class ModuleTestCaseAttribute(params object?[] arguments) : Attribute, IModuleTestDataAttribute {

    /// <summary>
    /// The arguments for this row, covering the method's leading parameters in order.
    /// </summary>
    public object?[] Arguments {
        get;
    } = arguments;

    /// <summary>
    /// Overrides the name this row is reported under. Defaults to the method name followed by the
    /// row's arguments, which is what tells one row from another in a test explorer.
    /// </summary>
    public string? TestName {
        get;
        set;
    }

    /// <inheritdoc />
    public IEnumerable<object?[]> GetRows(MethodInfo method) => [Arguments];
}
