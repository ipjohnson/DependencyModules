using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Runtime.Attributes;

/// <summary>
///   Register the attributed type and then all interfaces
///   will be registered pointing to the implementation registration
///   allowing for the same instance to be returned for multiple interfaces
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public class CrossWireServiceAttribute : Attribute, IServiceRegistrationAttribute {

    /// <inheritdoc />
    public object? Key {
        get;
        set;
    }
    
    /// <inheritdoc />
    [Browsable(false)]
    Type? IServiceRegistrationAttribute.As {
        get;
        set;
    }
    
    /// <inheritdoc />
    public ServiceLifetime Lifetime {
        get;
        set;
    }
    
    /// <summary>
    ///     Which method type to use, 
    /// </summary>
    public RegistrationType Using { get; set; } = RegistrationType.Add;
    
    /// <summary>
    ///     DependencyModule realm that this type should be associated with
    /// </summary>
    public Type? Realm { get; set; }
}