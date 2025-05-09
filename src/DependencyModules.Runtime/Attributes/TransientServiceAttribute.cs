using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Runtime.Attributes;

/// <summary>
///     Register service or factory as Transient
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public class TransientServiceAttribute : BaseServiceAttribute {
    
    /// <inheritdoc />
    protected override ServiceLifetime Lifetime => ServiceLifetime.Transient;
}