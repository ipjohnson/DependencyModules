using System.Reflection;
using DependencyModules.Testing.Attributes.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using VerificationGlue.Tests;

namespace DependencyModules.VerificationGlue.Impl;

/// <summary>
/// Supplies a test parameter from a module container.
/// </summary>
internal static class ParameterResolution
{
    private const string DependencyModulesMock =
        "DependencyModules.Testing.Attributes.MockAttribute";

    /// <summary>
    /// The service for the parameter's type and key, or an instance of an unregistered concrete
    /// type built from the container.
    /// </summary>
    /// <param name="services">The container's services.</param>
    /// <param name="parameter">The parameter.</param>
    /// <param name="methodContext">The test, for an <see cref="IInjectValueAttribute"/>.</param>
    /// <param name="created">Receives an instance this built, which the container disposes.</param>
    /// <param name="value">The value.</param>
    public static bool TryResolve(
        IServiceProvider services,
        TestParameter parameter,
        VerificationGlueTestMethodContext? methodContext,
        Action<object> created,
        out object? value
    )
    {
        RejectDependencyModulesMock(parameter);

        if (parameter.ServiceKey is { } key)
        {
            value = (services as IKeyedServiceProvider)?.GetKeyedService(parameter.Type, key);

            return value is not null;
        }

        value = services.GetService(parameter.Type);

        if (value is not null)
        {
            return true;
        }

        if (!CanConstruct(parameter))
        {
            return false;
        }

        value = ActivatorUtilities.CreateInstance(
            services,
            parameter.Type,
            InjectedValues(services, parameter, methodContext)
        );

        created(value);

        return true;
    }

    /// <remarks>
    /// The <c>[Mock]</c> of DependencyModules.Testing is not the <c>[Mock]</c> of
    /// VerificationGlue.Mocks, and VerificationGlue.Tests creates no mock for it. Without this the
    /// parameter would get the real service, and the test would set up a mock that nothing uses.
    /// </remarks>
    private static void RejectDependencyModulesMock(TestParameter parameter)
    {
        foreach (var attribute in parameter.Attributes)
        {
            if (attribute.GetType().FullName == DependencyModulesMock)
            {
                throw new InvalidOperationException(
                    $"Parameter '{parameter.Name}' has the [Mock] of DependencyModules.Testing. "
                        + "A VerificationGlue.Tests test uses [Mock] from VerificationGlue.Mocks. "
                        + "When both namespaces are imported, write "
                        + "'using MockAttribute = VerificationGlue.Mocks.MockAttribute;'."
                );
            }
        }
    }

    /// <summary>
    /// True for a concrete class that no registration supplies, which the container can build so a
    /// test can name the class under test. The built-in values of VerificationGlue.Tests, strings,
    /// and a parameter with a default value are left to the runner.
    /// </summary>
    private static bool CanConstruct(TestParameter parameter)
    {
        var type = parameter.Type;

        if (
            parameter.HasDefaultValue
            || !type.IsClass
            || type.IsAbstract
            || type.IsArray
            || type == typeof(string)
            || typeof(Delegate).IsAssignableFrom(type)
            || type.ContainsGenericParameters
        )
        {
            return false;
        }

        return type.Namespace is not { } ns
            || !(
                ns == "VerificationGlue"
                || ns.StartsWith("VerificationGlue.", StringComparison.Ordinal)
            );
    }

    private static object[] InjectedValues(
        IServiceProvider services,
        TestParameter parameter,
        VerificationGlueTestMethodContext? methodContext
    )
    {
        object[] values = [];
        ParameterInfo? parameterInfo = null;

        foreach (var attribute in parameter.Attributes)
        {
            if (attribute is IInjectValueAttribute injectValue)
            {
                parameterInfo ??=
                    methodContext?.ParameterInfoOf(parameter)
                    ?? throw new InvalidOperationException(
                        $"Parameter '{parameter.Name}' has [{attribute.GetType().Name}], which needs "
                            + "the reflection ParameterInfo of the test method, and the test method "
                            + "could not be found."
                    );

                values = injectValue.ProvideValue(services, parameterInfo);
            }
        }

        return values;
    }
}
