namespace DependencyModules.Runtime.Attributes;

/// <summary>
///     Register service as singleton
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class SingletonServiceAttribute : BaseServiceAttribute { }