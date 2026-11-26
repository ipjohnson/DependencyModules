using System.Collections.Concurrent;
using System.Reflection;
using DependencyModules.Testing.Attributes.Interfaces;
using VerificationGlue.Tests;

namespace DependencyModules.VerificationGlue.Impl;

/// <summary>
/// The <see cref="ITestMethodContext"/> that the DependencyModules.Testing attributes receive
/// under VerificationGlue.Tests.
/// </summary>
/// <remarks>
/// <para>
/// VerificationGlue.Tests finds tests without reflection, so a container is given the test's UID
/// and parameters and no <see cref="MethodInfo"/>. This finds the method from the UID, which is
/// the type, the method name and the parameter types, when an attribute first asks for it.
/// </para>
/// <para>
/// A class or assembly container is built for no single test, so <see cref="Method"/> throws
/// there.
/// </para>
/// </remarks>
internal sealed class VerificationGlueTestMethodContext(TestContainerContext context)
    : ITestMethodContext
{
    private static readonly ConcurrentDictionary<string, Type?> TypesByName = new(
        StringComparer.Ordinal
    );

    private readonly Lazy<MethodInfo?> _method = new(() => Find(context));

    public MethodInfo Method =>
        _method.Value
        ?? throw new InvalidOperationException(
            context.Scope.Kind == TestScopeKind.Test
                ? $"The test method of {context.Uid} could not be found by reflection."
                : $"The {context.Scope.Kind.ToString().ToLowerInvariant()} container of "
                    + $"{context.Uid} is built for no single test method. An attribute that reads "
                    + "the test method works only for a container with Scope = ContainerScope.Test "
                    + "or on a method."
        );

    /// <summary>
    /// The method, or null when there is none or it cannot be found.
    /// </summary>
    public MethodInfo? MethodOrNull => _method.Value;

    public IReadOnlyList<Attribute> Attributes => context.Attributes;

    /// <summary>
    /// The reflection parameter of a test method or test class constructor parameter.
    /// </summary>
    public ParameterInfo? ParameterInfoOf(TestParameter parameter)
    {
        if (MethodOrNull is not { } method)
        {
            return null;
        }

        if (!parameter.IsConstructorParameter)
        {
            var parameters = method.GetParameters();

            return parameter.Position < parameters.Length ? parameters[parameter.Position] : null;
        }

        // The constructor VerificationGlue.Tests uses: the accessible one with the most parameters.
        var constructor = method
            .DeclaringType?.GetConstructors()
            .OrderByDescending(candidate => candidate.GetParameters().Length)
            .FirstOrDefault();

        var constructorParameters = constructor?.GetParameters() ?? [];

        return parameter.Position < constructorParameters.Length
            ? constructorParameters[parameter.Position]
            : null;
    }

    private static MethodInfo? Find(TestContainerContext context)
    {
        if (context.Scope.Kind != TestScopeKind.Test)
        {
            return null;
        }

        var uid = context.Uid;
        var open = uid.IndexOf('(');
        var qualified = open < 0 ? uid : uid[..open];
        var dot = qualified.LastIndexOf('.');

        if (dot <= 0)
        {
            return null;
        }

        var type = TypesByName.GetOrAdd(qualified[..dot], FindType);
        var methodName = qualified[(dot + 1)..];
        var parameterTypes = context
            .Parameters.Where(parameter => !parameter.IsConstructorParameter)
            .OrderBy(parameter => parameter.Position)
            .Select(parameter => parameter.Type)
            .ToArray();

        return type?.GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static,
            parameterTypes
        );
    }

    /// <remarks>
    /// The UID writes a nested type with a dot, where reflection writes a plus. The test assembly
    /// is the entry assembly of a VerificationGlue.Tests application, so it is searched first.
    /// </remarks>
    private static Type? FindType(string name)
    {
        var entry = Assembly.GetEntryAssembly();
        var assemblies = AppDomain
            .CurrentDomain.GetAssemblies()
            .OrderBy(assembly => assembly == entry ? 0 : 1);

        foreach (var assembly in assemblies)
        {
            if (assembly.IsDynamic)
            {
                continue;
            }

            if (assembly.GetType(name) is { } direct)
            {
                return direct;
            }
        }

        foreach (var assembly in assemblies)
        {
            if (assembly.IsDynamic)
            {
                continue;
            }

            Type[] types;

            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                types = exception.Types.OfType<Type>().ToArray();
            }

            foreach (var type in types)
            {
                if (type.IsNested && type.FullName?.Replace('+', '.') == name)
                {
                    return type;
                }
            }
        }

        return null;
    }
}
