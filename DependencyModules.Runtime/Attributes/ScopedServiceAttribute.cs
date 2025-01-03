namespace DependencyModules.Runtime.Attributes;

/// <summary>
/// Register service as scoped
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class ScopedServiceAttribute : BaseServiceAttribute { }