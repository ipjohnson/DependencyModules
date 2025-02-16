using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Runtime.Attributes;

/// <summary>
///     Register service as Transient
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public class TransientServiceAttribute : BaseServiceAttribute {

    protected override ServiceLifetime Lifetime => ServiceLifetime.Transient;
}