using DependencyModules.Runtime.Conventions;
using DependencyModules.Runtime.Attributes;

namespace SutProject.Tests.ConventionTests;

// Every convention declared here matches at least one type on purpose. A convention that matches
// nothing is DM0005, a warning, and this solution builds under a zero-warning gate.

// ---------------------------------------------------------------------------
// Direct declaration, and reach through interface inheritance.
// ---------------------------------------------------------------------------

public interface IConventionService {
    string Name { get; }
}

/// <summary>Extends the scanned interface, so implementing it is a declared match.</summary>
public interface IAuditedConventionService : IConventionService { }

public class DirectService : IConventionService {
    public string Name => "direct";
}

public class InheritedService : IAuditedConventionService {
    public string Name => "inherited";
}

// ---------------------------------------------------------------------------
// Reach through a base class, which takes an opt-in. Both modules below scan this same
// interface: one type declares it directly and always matches, the other only reaches it
// through a base class and matches only with IncludeBaseClasses().
// ---------------------------------------------------------------------------

public interface IBaseClassReachService {
    string Name { get; }
}

public class DirectReachService : IBaseClassReachService {
    public string Name => "direct-reach";
}

public abstract class ReachServiceBase : IBaseClassReachService {
    public abstract string Name { get; }
}

public class ThroughBaseClass : ReachServiceBase {
    public override string Name => "through-base";
}

// ---------------------------------------------------------------------------
// Open generic scanned against concrete closings.
// ---------------------------------------------------------------------------

public interface IConventionHandler<TIn, TOut> {
    TOut Handle(TIn input);
}

public class CreateOrder { }

public class RenameOrder { }

public class OrderId {
    public int Value { get; set; }
}

public class CreateOrderHandler : IConventionHandler<CreateOrder, OrderId> {
    public OrderId Handle(CreateOrder input) => new() { Value = 1 };
}

public class RenameOrderHandler : IConventionHandler<RenameOrder, OrderId> {
    public OrderId Handle(RenameOrder input) => new() { Value = 2 };
}

// ---------------------------------------------------------------------------
// A generic implementation passing its own parameter through, which registers open.
// ---------------------------------------------------------------------------

public interface IConventionCache<T> {
    string Describe();
}

public class PassThroughCache<T> : IConventionCache<T> {
    public string Describe() => "open:" + typeof(T).Name;
}

// ---------------------------------------------------------------------------
// An open generic reached through interface inheritance, which has to register against the
// closed construction the type actually implements.
// ---------------------------------------------------------------------------

public interface IConventionStore<T> {
    string Describe();
}

public interface IAuditedStore<T> : IConventionStore<T> { }

public class StringStore : IAuditedStore<string> {
    public string Describe() => "audited:string";
}

// ---------------------------------------------------------------------------
// An explicit attribute always wins; the convention picks up only the unattributed type.
// ---------------------------------------------------------------------------

public interface IAttributeWinsService {
    string Name { get; }
}

[SingletonService]
public class AttributedService : IAttributeWinsService {
    public string Name => "attributed";
}

public class ByConventionService : IAttributeWinsService {
    public string Name => "by-convention";
}

// ---------------------------------------------------------------------------
// Modules.
// ---------------------------------------------------------------------------

/// <summary>
/// Default reach: a type matches by declaring the service type, or by declaring an interface that
/// extends it.
/// </summary>
[DependencyModule]
public partial class ConventionSutModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll<IConventionService>().AsSingleton();
        conventions.RegisterAll(typeof(IConventionHandler<,>)).AsTransient();
        conventions.RegisterAll(typeof(IConventionCache<>)).AsScoped();
        conventions.RegisterAll(typeof(IConventionStore<>)).AsSingleton();
        conventions.RegisterAll<IAttributeWinsService>().AsTransient();
    }
}

/// <summary>Base-class hop off, which is the default. Only the direct implementation matches.</summary>
[DependencyModule]
public partial class ConventionNoBaseClassModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll<IBaseClassReachService>().AsSingleton();
    }
}

/// <summary>The same scan with the base-class hop opted in.</summary>
[DependencyModule]
public partial class ConventionBaseClassModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll<IBaseClassReachService>().AsSingleton().IncludeBaseClasses();
    }
}
