namespace DependencyModules.Testing.Impl;

/// <summary>
/// An instance that the test's container returns for a service type and an optional key, such as a
/// mock.
/// </summary>
/// <remarks>
/// <see cref="ModuleTestComposition.BuildAsync"/> registers each override after the modules and the
/// test's setup attributes, so an override replaces a <c>[TestExport]</c> for the same service. A
/// test parameter of the same type and key gets the override, and the parameter's
/// <see cref="Attributes.Interfaces.ITestParameterValueProvider"/> attributes do not run.
/// </remarks>
/// <param name="ServiceType">The service type to register.</param>
/// <param name="Instance">The instance to return. It must be assignable to <paramref name="ServiceType"/>.</param>
/// <param name="ServiceKey">The service key, or null for an unkeyed registration.</param>
public sealed record TestServiceOverride(Type ServiceType, object Instance, object? ServiceKey);
