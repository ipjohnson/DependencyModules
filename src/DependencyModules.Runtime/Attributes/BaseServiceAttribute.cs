using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Runtime.Attributes;

/// <summary>
/// Defines the method of registration for services in a dependency injection container.
/// </summary>
public enum RegistrationType {
    /// <summary>
    /// Registers the service unconditionally, adding a new registration even if another service of the same type exists.
    /// </summary>
    Add,

    /// <summary>
    /// Registers the service only if no existing service of the given type is already registered.
    /// Ensures that only one instance of the service registration exists.
    /// </summary>
    Try,

    /// <summary>
    /// Attempts to add the service while allowing multiple implementations for the same service type.
    /// This is useful for scenarios with collections of implementations or plugin systems.
    /// </summary>
    TryEnumerable,

    /// <summary>
    /// Replaces an existing service registration with the new one.
    /// Typically used to override default implementations in the dependency injection container.
    /// </summary>
    Replace
}

/// <summary>
/// The shape of a service registration attribute: what it registers as, under what key, with what
/// lifetime, and by which registration method.
/// </summary>
/// <remarks>
/// <para>
/// <b>Descriptive, not a discovery mechanism.</b> Implementing this interface does not make an
/// attribute one the generator reads. Nothing in the generator tests for it — registration
/// attributes are matched by type, through the names a generator declares, because that is what
/// keeps them on <c>ForAttributeWithMetadataName</c> and therefore on Roslyn's attribute index.
/// Discovery by interface would mean a syntax provider over every node in the compilation, with the
/// transform re-running per keystroke, which is the cost this generator is built to avoid.
/// </para>
/// <para>
/// A framework wanting its own registration attributes to be first class declares them to its own
/// generator instead: derive from <c>BaseSourceGenerator</c>, name the module attribute in
/// <c>ModuleAttributeTypes()</c>, and collect the rest through <c>AttributeSourceGenerators()</c>.
/// A generator built that way stacks with this one for about 0.4 ms per keystroke.
/// </para>
/// <para>
/// What the interface is for is reading a registration uniformly at run time or through reflection
/// — <c>[SingletonService]</c>, <c>[ScopedService]</c>, <c>[TransientService]</c> and
/// <c>[CrossWireService]</c> all answer to it — and giving those attributes one place to define what
/// a registration consists of.
/// </para>
/// </remarks>
public interface  IServiceRegistrationAttribute {
    /// <summary>
    /// Gets or sets a key used for service registration,
    /// typically to distinguish between multiple registrations
    /// of the same service type or to categorize services.
    /// </summary>
    object? Key { 
        get => null;
        set {}
    }

    /// <summary>
    /// Gets or sets the type that the service should be registered as in the dependency injection container.
    /// Typically used to specify an interface or a base type that the implementation will be registered and resolved as.
    /// </summary>
    Type? As { 
        get => null;
        set { } 
    }

    /// <summary>
    /// Gets or sets the lifetime of the service in the dependency injection container,
    /// determining the duration for which the service instance is retained.
    /// Common lifetimes include Transient, Scoped, and Singleton.
    /// </summary>
    ServiceLifetime Lifetime {
        get => ServiceLifetime.Transient;
        set { }
    }

    /// <summary>
    /// Gets or sets the registration type that specifies the method of service registration
    /// in a dependency injection container, such as adding a new service, trying to add a
    /// service if it doesn't already exist, adding a service to an enumerable, or replacing
    /// an existing service.
    /// </summary>
    RegistrationType Using {
        get => RegistrationType.Add;
        set { }
    }
}

/// <summary>
/// Serves as a base attribute for defining service registration metadata in dependency
/// injection frameworks. It provides shared properties and behavior for derived service
/// registration attributes, such as specifying the service type, registration type, and
/// associated service lifetime.
/// </summary>
public abstract class BaseServiceAttribute : Attribute, IServiceRegistrationAttribute {
    
    /// <inheritdoc />
    public object? Key { get; set; }
    
    /// <inheritdoc />
    public Type? As { get; set; }
    
    /// <inheritdoc />
    public RegistrationType Using { get; set; } = RegistrationType.Add;


    /// <summary>
    /// Gets or sets the module or scope under which the service should be registered.
    /// This property allows organizing or segregating service registrations across different logical groups
    /// or runtime modules.
    /// </summary>
    public Type? Realm { get; set; }
    
    /// <inheritdoc />
    [Browsable(false)]
    ServiceLifetime IServiceRegistrationAttribute.Lifetime {
        get => Lifetime;
        set => throw new Exception("Setting lifetime is not supported");
    }

    /// <summary>
    /// Gets the service lifetime indicating how the service will be instantiated
    /// and managed within the dependency injection container.
    /// The value determines whether the service is registered as a transient, scoped,
    /// or singleton lifetime.
    /// </summary>
    protected abstract ServiceLifetime Lifetime { get; }
}