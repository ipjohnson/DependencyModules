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
    object? Key { 
        get => null;
        set {}
    }
    
    Type? As { 
        get => null;
        set { } 
    }

    ServiceLifetime Lifetime {
        get => ServiceLifetime.Transient;
        set { }
    }

    RegistrationType Using {
        get => RegistrationType.Add;
        set { }
    }
}

public abstract class BaseServiceAttribute : Attribute, IServiceRegistrationAttribute {
    /// <summary>
    ///     Key to use for DI registration
    /// </summary>
    public object? Key { get; set; }

    /// <summary>
    ///     Service type to register
    /// </summary>
    public Type? As { get; set; }
    
    /// <summary>
    ///     Which method type to use, 
    /// </summary>
    public RegistrationType Using { get; set; } = RegistrationType.Add;

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