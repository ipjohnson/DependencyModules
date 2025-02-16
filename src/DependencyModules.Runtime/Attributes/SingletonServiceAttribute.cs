using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Runtime.Attributes;

/// <summary>
///     Register service as singleton
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public class SingletonServiceAttribute : BaseServiceAttribute {
    protected override ServiceLifetime Lifetime => ServiceLifetime.Singleton;
}