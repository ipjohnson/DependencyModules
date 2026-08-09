using DependencyModules.Conventions;
using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interception;

namespace SutProject.Tests.ConventionTests;

// Conventions crossed with everything else the library does: interception, decoration with an
// order, keyed registration, realms, module composition, environment conditions — and the type
// shapes people actually write, which are not all plain classes.
//
// Every convention here matches at least one type on purpose. A convention that matches nothing is
// DM0005, a warning, and this solution builds under a zero-warning gate.

// ---------------------------------------------------------------------------
// Conventions and interception. Neither knows about the other; [Intercept] is not a service
// attribute, so the class stays a convention candidate.
// ---------------------------------------------------------------------------

/// <summary>Records what the interceptor saw.</summary>
[SingletonService]
public class InterceptLog {
    public List<string> Lines { get; } = new();
}

[SingletonService]
public class RecordingInterceptor(InterceptLog log) : IInterceptor {
    public TResult Intercept<TResult>(InvocationContext<TResult> context) {
        log.Lines.Add("intercepted " + context.Caller.MemberName);

        return context.Proceed();
    }
}

public interface IInterceptedByConvention {
    string Work();
}

[Intercept(typeof(RecordingInterceptor))]
public class InterceptedByConvention : IInterceptedByConvention {
    public string Work() => "worked";
}

[DependencyModule]
public partial class ConventionInterceptModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll<IInterceptedByConvention>().AsSingleton();
    }
}

// ---------------------------------------------------------------------------
// Two decorators with an order, over convention-registered services.
// ---------------------------------------------------------------------------

public interface IOrdered {
    string Describe();
}

public class OrderedCore : IOrdered {
    public string Describe() => "core";
}

[Decorator(Order = 10)]
public class InnerOrdered(IOrdered inner) : IOrdered {
    public string Describe() => $"inner({inner.Describe()})";
}

[Decorator(Order = 20)]
public class OuterOrdered(IOrdered inner) : IOrdered {
    public string Describe() => $"outer({inner.Describe()})";
}

[DependencyModule]
public partial class ConventionOrderedDecoratorModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll<IOrdered>().AsSingleton();
    }
}

// ---------------------------------------------------------------------------
// A keyed convention registration, decorated.
// ---------------------------------------------------------------------------

public interface IKeyedAndDecorated {
    string Describe();
}

public class KeyedCore : IKeyedAndDecorated {
    public string Describe() => "core";
}

[Decorator]
public class KeyedWrapper(IKeyedAndDecorated inner) : IKeyedAndDecorated {
    public string Describe() => $"wrapped({inner.Describe()})";
}

[DependencyModule]
public partial class ConventionKeyedDecoratedModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll<IKeyedAndDecorated>().WithKey("main").AsSingleton();
    }
}

// ---------------------------------------------------------------------------
// Type shapes that are not plain classes: records, nested types, primary constructors.
// ---------------------------------------------------------------------------

public interface IShaped {
    string Name { get; }
}

public record ShapedRecord : IShaped {
    public string Name => "record";
}

public record struct NotACandidate;

public class Outer {
    public class NestedShaped : IShaped {
        public string Name => "nested";
    }
}

public interface IShapedDependency {
    string Value { get; }
}

public class ShapedDependency : IShapedDependency {
    public string Value => "dep";
}

/// <summary>Primary constructor, injected from another convention registration.</summary>
public class PrimaryConstructorShaped(IShapedDependency dependency) : IShaped {
    public string Name => "primary-" + dependency.Value;
}

[DependencyModule]
public partial class ConventionShapesModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll<IShaped>().AsSingleton();
        conventions.RegisterAll<IShapedDependency>().AsSingleton();
    }
}

// ---------------------------------------------------------------------------
// A realm module. OnlyRealm means it takes nothing that did not name it.
// ---------------------------------------------------------------------------

public interface IRealmScoped {
    string Name { get; }
}

public class RealmScoped : IRealmScoped {
    public string Name => "realm";
}

[DependencyModule(OnlyRealm = true)]
public partial class ConventionRealmModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll<IRealmScoped>().AsSingleton();
    }
}

// ---------------------------------------------------------------------------
// A module composed from another. The conventions of a dependency come along with it.
// ---------------------------------------------------------------------------

public interface IComposedService {
    string Name { get; }
}

public class ComposedService : IComposedService {
    public string Name => "composed";
}

[DependencyModule]
public partial class ConventionDependencyModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll<IComposedService>().AsSingleton();
    }
}

/// <summary>Names the module above, so its convention registrations arrive with it.</summary>
[ConventionDependencyModule]
[DependencyModule]
public partial class ConventionCompositionModule;

// ---------------------------------------------------------------------------
// Environment conditions on convention candidates.
// ---------------------------------------------------------------------------

public interface IConditionalByConvention {
    string Name { get; }
}

public class AlwaysConditional : IConditionalByConvention {
    public string Name => "always";
}

[IfEnvironment("Development")]
public class DevelopmentOnlyConditional : IConditionalByConvention {
    public string Name => "development";
}

[DependencyModule]
public partial class ConventionConditionalModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll<IConditionalByConvention>().AsSingleton();
    }
}
