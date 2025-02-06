using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Runtime.Attributes;

/// <summary>
///     Register service as singleton
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class SingletonServiceAttribute : BaseServiceAttribute {
    protected override ServiceLifetime Lifetime => ServiceLifetime.Singleton;
}