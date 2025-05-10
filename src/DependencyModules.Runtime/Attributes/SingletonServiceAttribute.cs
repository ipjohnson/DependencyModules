using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Runtime.Attributes;

/// <summary>
///     Register service or factory as singleton
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public class SingletonServiceAttribute : BaseServiceAttribute {
    /// <inheritdoc />
    protected override ServiceLifetime Lifetime => ServiceLifetime.Singleton;
}