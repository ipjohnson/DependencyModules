namespace DependencyModules.Runtime.Attributes;

/// <summary>
///     Register service as singleton
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class SingletonServiceAttribute : BaseServiceAttribute { }