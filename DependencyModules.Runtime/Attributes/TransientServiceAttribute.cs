namespace DependencyModules.Runtime.Attributes;

/// <summary>
///     Register service as Transient
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class TransientServiceAttribute : BaseServiceAttribute { }