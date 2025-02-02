namespace DependencyModules.Runtime.Attributes;

/// <summary>
///     Register service as scoped
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class ScopedServiceAttribute : BaseServiceAttribute { }