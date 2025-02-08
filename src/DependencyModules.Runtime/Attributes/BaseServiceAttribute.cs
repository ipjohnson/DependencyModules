using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Runtime.Attributes;

public enum RegistrationType {
    Add,
    Try,
    TryEnumerable,
    Replace
}

public interface  IServiceRegistrationAttribute {
    object? Key { get; set; }
    
    Type? ServiceType { get; set; }
    
    ServiceLifetime Lifetime { get; set; }   
}

public abstract class BaseServiceAttribute : Attribute, IServiceRegistrationAttribute {
    /// <summary>
    ///     Key to use for DI registration
    /// </summary>
    public object? Key { get; set; }

    /// <summary>
    ///     Service type to register
    /// </summary>
    public Type? ServiceType { get; set; }
    
    /// <summary>
    ///     Which method type to use, 
    /// </summary>
    public RegistrationType With { get; set; } = RegistrationType.Add;

    /// <summary>
    ///     DependencyModule realm that this type should be associated with
    /// </summary>
    public Type? Realm { get; set; }
    
    [Browsable(false)]
    ServiceLifetime IServiceRegistrationAttribute.Lifetime {
        get => Lifetime;
        set => throw new Exception("Setting lifetime is not supported");
    }

    protected abstract ServiceLifetime Lifetime { get; }
}