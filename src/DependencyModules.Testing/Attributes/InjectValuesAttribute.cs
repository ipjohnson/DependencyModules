using System.Reflection;
using DependencyModules.Testing.Attributes.Interfaces;

namespace DependencyModules.Testing.Attributes;

/// <summary>
/// Specifies a custom attribute used for injecting specified values into
/// method parameters during dependency resolution and test execution.
/// The attribute implements <see cref="IInjectValueAttribute"/> to provide
/// the capability of supplying predefined values at runtime. It is typically
/// used in scenarios such as dependency injection testing where custom values
/// are required for certain components that are not directly resolved from
/// the dependency injection container.
/// This attribute can be applied to method parameters and is resolved when
/// the method is invoked, enabling runtime value injection.
/// </summary>
/// <remarks>
/// The values are the parameter type's <i>constructor arguments</i>, not the parameter's value.
/// They are combined with whatever the container supplies, so
/// <c>[InjectValues("wholesale")] OrderRequest</c> constructs an
/// <c>OrderRequest(IValidator&lt;PlaceOrder&gt;, string)</c> with the validator resolved and the
/// string supplied. A parameter that should simply <i>be</i> a value wants a data row —
/// <c>[InlineData]</c> or <c>[TestCase]</c> — which composes with <c>[ModuleTest]</c>; asking for a
/// bare <c>string</c> here fails, because <c>System.String</c> has no matching constructor.
///
/// Restricted to parameters. Applied anywhere else it is never read, and the test then fails inside
/// ActivatorUtilities naming the parameter's type rather than the misplaced attribute.
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter)]
public class InjectValuesAttribute(params object[] value) : Attribute, IInjectValueAttribute {

    /// <summary>
    /// Provides the specified values for a method parameter during dependency
    /// injection and test execution. This method is part of the
    /// <see cref="IInjectValueAttribute"/> interface implementation and is
    /// responsible for supplying predefined runtime values to the method parameters.
    /// </summary>
    /// <param name="serviceProvider">
    /// The service provider instance used for resolving dependencies. It may be
    /// utilized to supply services during the injection process.
    /// </param>
    /// <param name="parameter">
    /// The parameter information that represents the method parameter being resolved
    /// for value injection. This includes metadata about the parameter.
    /// </param>
    /// <returns>
    /// An array of objects representing the values to be injected into the specified
    /// method parameter.
    /// </returns>
    public object[] ProvideValue(IServiceProvider serviceProvider, ParameterInfo parameter) {
        return value;
    }
}