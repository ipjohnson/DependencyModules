using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Runtime.Attributes;

/// <summary>
///     Register service as Transient
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class TransientServiceAttribute : BaseServiceAttribute {

    protected override ServiceLifetime Lifetime => ServiceLifetime.Transient;
}