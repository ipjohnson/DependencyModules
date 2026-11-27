using System.Reflection;
using DependencyModules.Testing.Attributes.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Testing.Impl;

/// <summary>
/// Works out what to pass a test method, and what its parameters need registered to make that
/// possible.
/// </summary>
/// <remarks>
/// A test framework integration owns discovery, execution and disposal; none of that is shared. What
/// is shared is the rule for turning a parameter list into arguments, and it is more involved than it
/// looks — a parameter may be supplied by an attribute on itself, resolved from the container, keyed,
/// or constructed on the spot from types the container does know. Every integration needs the same
/// answers, so this holds them once.
///
/// Used in two phases either side of the container being built, and in that order:
/// <see cref="SetupServiceCollection"/> while registrations can still be added, then
/// <see cref="ResolveArgumentsAsync"/> once there is a provider to resolve from. One instance belongs
/// to one container: a data-driven test that builds a container per row wants a resolver per row too.
/// </remarks>
public sealed class TestParameterResolver
{
    private readonly ITestMethodContext _testMethod;

    // ParameterInfo compares by reference, and two threads that call GetParameters on the same
    // method for the first time can each get a different array. Both phases use this one array.
    private readonly ParameterInfo[] _parameters;
    private readonly Dictionary<ParameterInfo, List<ITestParameterValueProvider>> _valueProviders =
        new();
    private readonly Func<ParameterInfo, bool>? _skip;
    private bool _setupRan;

    /// <summary>
    /// Creates a resolver for one test method and one container.
    /// </summary>
    /// <param name="testMethod">The test whose parameters are being supplied.</param>
    public TestParameterResolver(ITestMethodContext testMethod)
    {
        _testMethod = testMethod;
        _parameters = testMethod.Method.GetParameters();
    }

    /// <summary>
    /// Creates a resolver whose <paramref name="skip"/> parameters run no value provider in either
    /// phase. <see cref="ModuleTestComposition"/> skips the parameters that an override supplies.
    /// </summary>
    internal TestParameterResolver(ITestMethodContext testMethod, Func<ParameterInfo, bool> skip)
        : this(testMethod)
    {
        _skip = skip;
    }

    /// <summary>
    /// Lets every parameter register what it needs, before the container is built.
    /// </summary>
    /// <remarks>
    /// This is the half that makes <c>[Mock]</c> more than a convenience. Registering during setup
    /// means the substitute is in place before anything resolves, so the service under test is
    /// constructed against it — rather than the test being handed a double nothing else can see.
    /// </remarks>
    /// <param name="serviceCollection">The collection backing the test's container.</param>
    public void SetupServiceCollection(IServiceCollection serviceCollection)
    {
        foreach (var parameterInfo in _parameters)
        {
            var providers =
                _skip != null && _skip(parameterInfo)
                    ? []
                    : parameterInfo
                        .GetCustomAttributes()
                        .OfType<ITestParameterValueProvider>()
                        .ToList();

            _valueProviders.Add(parameterInfo, providers);

            foreach (var valueProvider in providers)
            {
                valueProvider.SetupServiceCollection(_testMethod, serviceCollection, parameterInfo);
            }
        }

        _setupRan = true;
    }

    /// <summary>
    /// Produces the full argument list for the test method.
    /// </summary>
    /// <remarks>
    /// The first <paramref name="data"/>.Length parameters are taken from the data row as given —
    /// that is what makes a data-driven test's own arguments win over the container. Each remaining
    /// parameter is then tried in turn against the attributes on it, the container, and finally
    /// direct construction.
    /// </remarks>
    /// <param name="serviceProvider">The test's container, fully built.</param>
    /// <param name="data">
    /// Arguments already fixed by a data row, matched to the leading parameters. Empty for a test
    /// with no data.
    /// </param>
    /// <returns>One argument per parameter, in declaration order.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="SetupServiceCollection"/> was not called first. Resolving without it
    /// would quietly skip every parameter attribute, so a <c>[Mock]</c> parameter would hand back the
    /// real service instead of a substitute.
    /// </exception>
    public async Task<object?[]> ResolveArgumentsAsync(
        IServiceProvider serviceProvider,
        object?[] data
    )
    {
        RequireSetup(nameof(ResolveArgumentsAsync));

        var parameterList = _parameters;
        var arguments = new List<object?>(data);

        for (var i = data.Length; i < parameterList.Length; i++)
        {
            var parameterInfo = parameterList[i];

            var value = await ResolveFromParameterProviders(parameterInfo, serviceProvider);

            arguments.Add(
                value ?? ResolveFromContainer(parameterInfo, serviceProvider, constructAny: true)
            );
        }

        return arguments.ToArray();
    }

    private void RequireSetup(string caller)
    {
        if (!_setupRan)
        {
            throw new InvalidOperationException(
                $"{nameof(SetupServiceCollection)} must be called before {caller}, "
                    + "while the service collection can still be added to."
            );
        }
    }

    /// <remarks>
    /// A provider returning null stands aside for the next one, so several attributes can sit on one
    /// parameter with the first that answers winning. <see cref="IServiceProvider"/> is special-cased
    /// because a test asking for the container itself cannot be resolved from it.
    /// </remarks>
    private async Task<object?> ResolveFromParameterProviders(
        ParameterInfo parameterInfo,
        IServiceProvider serviceProvider
    )
    {
        if (parameterInfo.ParameterType == typeof(IServiceProvider))
        {
            return serviceProvider;
        }

        return await ResolveFromValueProvidersAsync(
            _valueProviders[parameterInfo],
            parameterInfo,
            serviceProvider
        );
    }

    /// <summary>
    /// The value of the <see cref="ITestParameterValueProvider"/> attributes on one parameter, or
    /// null when none answers.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="parameterInfo"/> is not a parameter of the test method.
    /// </exception>
    internal Task<object?> GetParameterValueAsync(
        ParameterInfo parameterInfo,
        IServiceProvider serviceProvider
    )
    {
        RequireSetup(nameof(GetParameterValueAsync));

        if (!_valueProviders.TryGetValue(parameterInfo, out var providers))
        {
            // A MethodInfo from another lookup can hand out other ParameterInfo objects for the
            // same parameters.
            providers = _valueProviders
                .Where(entry =>
                    entry.Key.Position == parameterInfo.Position
                    && entry.Key.Member.Equals(parameterInfo.Member)
                )
                .Select(entry => entry.Value)
                .FirstOrDefault();
        }

        if (providers == null)
        {
            throw new ArgumentException(
                $"'{parameterInfo.Name}' is not a parameter of {_testMethod.Method.Name}.",
                nameof(parameterInfo)
            );
        }

        return ResolveFromValueProvidersAsync(providers, parameterInfo, serviceProvider);
    }

    private async Task<object?> ResolveFromValueProvidersAsync(
        List<ITestParameterValueProvider> providers,
        ParameterInfo parameterInfo,
        IServiceProvider serviceProvider
    )
    {
        foreach (var valueProvider in providers)
        {
            var value = await valueProvider.GetParameterValueAsync(
                _testMethod,
                serviceProvider,
                parameterInfo
            );

            if (value != null)
            {
                return value;
            }
        }

        return null;
    }

    /// <param name="parameterInfo">The parameter to resolve.</param>
    /// <param name="serviceProvider">The test's container.</param>
    /// <param name="constructAny">
    /// False to construct only a non-abstract class, and to return null for any other type that the
    /// container does not register.
    /// </param>
    internal static object? ResolveFromContainer(
        ParameterInfo parameterInfo,
        IServiceProvider serviceProvider,
        bool constructAny
    )
    {
        var keyedServicesAttribute = parameterInfo.GetCustomAttribute<FromKeyedServicesAttribute>();

        if (
            keyedServicesAttribute != null
            && serviceProvider is IKeyedServiceProvider keyedServiceProvider
        )
        {
            return keyedServiceProvider.GetKeyedService(
                parameterInfo.ParameterType,
                keyedServicesAttribute.Key
            );
        }

        var type = parameterInfo.ParameterType;

        return serviceProvider.GetService(type)
            ?? (
                constructAny || (type.IsClass && !type.IsAbstract)
                    ? ConstructValueFromType(parameterInfo, serviceProvider)
                    : null
            );
    }

    /// <summary>
    /// Builds an unregistered concrete type from the container, so a test can name the class under
    /// test directly rather than having to register it.
    /// </summary>
    /// <remarks>
    /// An <see cref="IInjectValueAttribute"/> on the parameter supplies the constructor arguments the
    /// container cannot work out for itself. The last one on the parameter wins.
    /// </remarks>
    private static object? ConstructValueFromType(
        ParameterInfo parameterInfo,
        IServiceProvider serviceProvider
    )
    {
        object[] parameterValues = [];

        foreach (var attribute in parameterInfo.GetCustomAttributes())
        {
            if (attribute is IInjectValueAttribute injectValueAttribute)
            {
                parameterValues = injectValueAttribute.ProvideValue(serviceProvider, parameterInfo);
            }
        }

        return ActivatorUtilities.CreateInstance(
            serviceProvider,
            parameterInfo.ParameterType,
            parameterValues
        );
    }
}
