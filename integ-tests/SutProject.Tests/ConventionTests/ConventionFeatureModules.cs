using DependencyModules.Runtime.Conventions;
using DependencyModules.Runtime.Attributes;
using SecondarySutProject;

namespace SutProject.Tests.ConventionTests;

// Types and modules for the selection, shape and decoration features, compiled by the real analyzer
// through MSBuild rather than driven in memory.
//
// Every convention here matches at least one type on purpose. A convention that matches nothing is
// DM0005, a warning, and this solution builds under a zero-warning gate.

// ---------------------------------------------------------------------------
// Shape: AsSelf, AsSelfWithInterfaces, AlsoAsSelf.
// ---------------------------------------------------------------------------

public interface IShapeService {
    string Name { get; }
}

public interface IAlsoShaped { }

public class SelfShaped : IShapeService {
    public string Name => "self";
}

public class CrossWiredShape : IShapeService, IAlsoShaped, IDisposable {
    public string Name => "crosswired";

    public void Dispose() { }
}

/// <summary>The only implementor of its interface, so AlsoAsSelf has one match to resolve.</summary>
public interface IAlsoSelfService { }

public class AlsoSelfShape : IShapeService, IAlsoSelfService {
    public string Name => "alsoself";
}

/// <summary>Registers the concrete type instead of the interface.</summary>
[DependencyModule]
public partial class ConventionAsSelfModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll<IShapeService>().AsSelf().AsSingleton();
    }
}

/// <summary>
/// Registers every interface the type reaches, sharing one instance — and skipping IDisposable,
/// which is reachable but is never what "as its interfaces" means.
/// </summary>
[DependencyModule]
public partial class ConventionCrossWireModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll<IAlsoShaped>().AsSelfWithInterfaces().AsSingleton();
    }
}

/// <summary>Registers the matched interface and the concrete type, sharing one instance.</summary>
[DependencyModule]
public partial class ConventionAlsoAsSelfModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll<IAlsoSelfService>().AlsoAsSelf().AsSingleton();
    }
}

// ---------------------------------------------------------------------------
// Filters: attribute, name, namespace.
// ---------------------------------------------------------------------------

/// <summary>Marks a type for the attribute filter.</summary>
[AttributeUsage(AttributeTargets.Class)]
public class PolicyAttribute : Attribute { }

public interface IFiltered {
    string Name { get; }
}

[Policy]
public class MarkedRepository : IFiltered {
    public string Name => "marked";
}

public class UnmarkedRepository : IFiltered {
    public string Name => "unmarked";
}

public class MarkedHelper : IFiltered {
    public string Name => "helper";
}

/// <summary>Only the type carrying the attribute.</summary>
[DependencyModule]
public partial class ConventionAttributeFilterModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll<IFiltered>().WithAttribute<PolicyAttribute>().AsSingleton();
    }
}

/// <summary>Only the names matching the glob.</summary>
[DependencyModule]
public partial class ConventionNameFilterModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll<IFiltered>().WithName("*Repository").AsSingleton();
    }
}

/// <summary>Selected by namespace alone, with no interface to match on.</summary>
[DependencyModule]
public partial class ConventionNamespaceModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll()
            .InNamespaceOf<NamespaceOnlyMarker>()
            .WithName("NamespaceOnly*")
            .AsSelf()
            .AsScoped();
    }
}

public class NamespaceOnlyMarker { }

public class NamespaceOnlyCalculator { }

// ---------------------------------------------------------------------------
// Shape: As<T> and AsMatchingInterface.
// ---------------------------------------------------------------------------

public interface INamedRoot { }

public interface IRenamer : INamedRoot { }

public class Renamer : IRenamer { }

public interface IExplicitTarget { }

public interface IExplicitSource { }

public class ExplicitlyRegistered : IExplicitSource, IExplicitTarget { }

/// <summary>Registers each match as the interface named after it.</summary>
[DependencyModule]
public partial class ConventionMatchingInterfaceModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll<INamedRoot>().AsMatchingInterface().AsSingleton();
    }
}

/// <summary>Registers every match as one named service type.</summary>
[DependencyModule]
public partial class ConventionAsModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll<IExplicitSource>().As<IExplicitTarget>().AsSingleton();
    }
}

// ---------------------------------------------------------------------------
// Registration type and service key.
// ---------------------------------------------------------------------------

public interface IKeyedService {
    string Name { get; }
}

public class KeyedOne : IKeyedService {
    public string Name => "keyed-one";
}

/// <summary>Registered under a service key.</summary>
[DependencyModule]
public partial class ConventionKeyModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll<IKeyedService>().WithKey("primary").AsSingleton();
    }
}

public interface ITriedService {
    string Name { get; }
}

public class TriedOne : ITriedService {
    public string Name => "one";
}

public class TriedTwo : ITriedService {
    public string Name => "two";
}

/// <summary>Try registers the service type once and skips the second match.</summary>
[DependencyModule]
public partial class ConventionUsingModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll<ITriedService>().Using(RegistrationType.Try).AsSingleton();
    }
}

// ---------------------------------------------------------------------------
// A type filling two roles, and one filling several closings of one interface.
// ---------------------------------------------------------------------------

public interface IFirstRole { }

public interface ISecondRole { }

public class TwoRoles : IFirstRole, ISecondRole { }

[DependencyModule]
public partial class ConventionTwoRolesModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll<IFirstRole>().AsSingleton();
        conventions.RegisterAll<ISecondRole>().AsScoped();
    }
}

public interface INotification<T> { }

public class OrderPlaced { }

public class OrderShipped { }

public class OrderEvents : INotification<OrderPlaced>, INotification<OrderShipped> { }

[DependencyModule]
public partial class ConventionClosingsModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll(typeof(INotification<>)).AsTransient();
    }
}

// ---------------------------------------------------------------------------
// Conventions and decorators together — the MediatR shape.
// ---------------------------------------------------------------------------

public interface IRequestHandler<TRequest, TResponse> {
    TResponse Handle(TRequest request);
}

public class CreateThing { }

public class RenameThing { }

public class ThingResult {
    public string Value { get; set; } = "";
}

public class CreateThingHandler : IRequestHandler<CreateThing, ThingResult> {
    public ThingResult Handle(CreateThing request) => new() { Value = "created" };
}

public class RenameThingHandler : IRequestHandler<RenameThing, ThingResult> {
    public ThingResult Handle(RenameThing request) => new() { Value = "renamed" };
}

/// <summary>Records what the decorator saw, so a test can prove it ran.</summary>
[SingletonService]
public class HandlerLog {
    public List<string> Lines { get; } = new();
}

/// <summary>
/// One decorator over every handler. It implements the interface it decorates, so a convention
/// scanning that interface must not match it — it is not a service.
/// </summary>
[Decorator]
public class LoggingRequestHandler<TRequest, TResponse>(
    IRequestHandler<TRequest, TResponse> inner, HandlerLog log)
    : IRequestHandler<TRequest, TResponse> {

    public TResponse Handle(TRequest request) {
        log.Lines.Add("handling " + typeof(TRequest).Name);

        return inner.Handle(request);
    }
}

[DependencyModule]
public partial class ConventionDecoratedHandlerModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll(typeof(IRequestHandler<,>)).AsScoped();
    }
}

// ---------------------------------------------------------------------------
// Scanning a referenced assembly, which is the one case with no syntax to read.
// ---------------------------------------------------------------------------

[DependencyModule]
public partial class ConventionAssemblyScanModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll<IPackagePolicy>()
            .InAssemblyOf<FirstPackagePolicy>()
            .AsSingleton();
    }
}
