using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Runtime.Attributes;

/// <summary>
///     Register service or factory as scoped
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public class ScopedServiceAttribute : BaseServiceAttribute {
    
    protected override ServiceLifetime Lifetime => ServiceLifetime.Scoped;
}