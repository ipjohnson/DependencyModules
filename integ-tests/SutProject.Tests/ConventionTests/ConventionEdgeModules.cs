using DependencyModules.Runtime.Conventions;
using DependencyModules.Runtime.Attributes;
using SecondarySutProject;
using SutProject.Tests.ConventionTests.Nested;

namespace SutProject.Tests.ConventionTests;

// The corners: module-level decoration, two modules over one interface, negative and exact
// filters, open generics resolved at several closings, lifetime and disposal, internal visibility,
// and a metadata scan with filters and a shape.
//
// Every convention here matches at least one type on purpose. A convention that matches nothing is
// DM0005, a warning, and this solution builds under a zero-warning gate.

// ---------------------------------------------------------------------------
// [Decorate] on the module rather than [Decorator] on the class. Different code path, and the one
// to use when the decorator or the service comes from an assembly you do not control.
// ---------------------------------------------------------------------------

public interface IModuleDecorated {
    string Describe();
}

public class ModuleDecoratedCore : IModuleDecorated {
    public string Describe() => "core";
}

/// <summary>Carries no [Decorator]; the module names it instead.</summary>
public class ModuleDecoratedWrapper(IModuleDecorated inner) : IModuleDecorated {
    public string Describe() => $"wrapped({inner.Describe()})";
}

[DependencyModule]
[Decorate(typeof(IModuleDecorated), typeof(ModuleDecoratedWrapper))]
public partial class ConventionModuleDecorateModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        // The wrapper implements the interface too, so it would match. Excluding it by name is the
        // cost of declaring decoration on the module rather than on the class.
        conventions.RegisterAll<IModuleDecorated>().WithoutName("*Wrapper").AsSingleton();
    }
}

// ---------------------------------------------------------------------------
// Two modules scanning one interface, composed into the same application.
// ---------------------------------------------------------------------------

public interface IShared {
    string Name { get; }
}

public class SharedFirst : IShared {
    public string Name => "first";
}

public class SharedSecond : IShared {
    public string Name => "second";
}

[DependencyModule]
public partial class ConventionSharedFirstModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll<IShared>().WithName("SharedFirst").AsSingleton();
    }
}

[DependencyModule]
public partial class ConventionSharedSecondModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll<IShared>().WithName("SharedSecond").AsSingleton();
        conventions.RegisterAll<IAlsoShaped>().WithoutName("SharedFirst").AsSingleton();
    }
}

// ---------------------------------------------------------------------------
// Exact versus prefix namespaces, and the negative form.
// ---------------------------------------------------------------------------

public interface INamespaceScanned {
    string Name { get; }
}

public class RootLevelScanned : INamespaceScanned {
    public string Name => "root";
}

/// <summary>Prefix filters reach into nested namespaces; exact ones do not.</summary>
[DependencyModule]
public partial class ConventionPrefixNamespaceModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll<INamespaceScanned>().InNamespaceOf<RootLevelScanned>().AsSingleton();
    }
}

[DependencyModule]
public partial class ConventionExactNamespaceModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll<INamespaceScanned>()
            .InExactNamespaces("SutProject.Tests.ConventionTests")
            .AsSingleton();
    }
}

[DependencyModule]
public partial class ConventionExcludedNamespaceModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll<INamespaceScanned>()
            .NotInNamespaceOf<NestedScanned>()
            .AsSingleton();
    }
}

// ---------------------------------------------------------------------------
// An open generic implementation, resolved at several closings.
// ---------------------------------------------------------------------------

public interface IOpenCache<T> {
    string Describe();
}

/// <summary>Closes nothing, so it registers as the open generic.</summary>
public class OpenPassThroughCache<T> : IOpenCache<T> {
    public string Describe() => "cache:" + typeof(T).Name;
}

[DependencyModule]
public partial class ConventionOpenGenericModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll(typeof(IOpenCache<>)).AsSingleton();
    }
}

// ---------------------------------------------------------------------------
// Lifetime, disposal, and an internal candidate.
// ---------------------------------------------------------------------------

public interface IScopedByConvention {
    Guid Id { get; }
}

public class ScopedByConvention : IScopedByConvention {
    public Guid Id { get; } = Guid.NewGuid();
}

public interface IDisposableByConvention {
    bool Disposed { get; }
}

public class DisposableByConvention : IDisposableByConvention, IDisposable {
    public bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;
}

/// <summary>Internal, and still a candidate — the compilation being built sees its own internals.</summary>
public interface IInternallyImplemented {
    string Name { get; }
}

internal class InternalCandidate : IInternallyImplemented {
    public string Name => "internal";
}

[DependencyModule]
public partial class ConventionLifetimeModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll<IScopedByConvention>().AsScoped();
        conventions.RegisterAll<IDisposableByConvention>().AsSingleton();
        conventions.RegisterAll<IInternallyImplemented>().AsSingleton();
    }
}

// ---------------------------------------------------------------------------
// A decorator whose own dependencies are convention-registered. Decoration builds through
// ActivatorUtilities, so everything but the inner instance is resolved from the container.
// ---------------------------------------------------------------------------

public interface IDecoratorDependency {
    string Value { get; }
}

public class DecoratorDependency : IDecoratorDependency {
    public string Value => "dep";
}

public interface IDependentlyDecorated {
    string Describe();
}

public class DependentlyDecoratedCore : IDependentlyDecorated {
    public string Describe() => "core";
}

[Decorator]
public class DependentlyDecorating(IDependentlyDecorated inner, IDecoratorDependency dependency)
    : IDependentlyDecorated {

    public string Describe() => $"{dependency.Value}({inner.Describe()})";
}

[DependencyModule]
public partial class ConventionDecoratorDependencyModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll<IDependentlyDecorated>().AsSingleton();
        conventions.RegisterAll<IDecoratorDependency>().AsSingleton();
    }
}

// ---------------------------------------------------------------------------
// A metadata scan narrowed by a filter and reshaped, rather than the plain form.
// ---------------------------------------------------------------------------

[DependencyModule]
public partial class ConventionFilteredScanModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll<IPackagePolicy>()
            .InAssemblyOf<FirstPackagePolicy>()
            .WithName("First*")
            .AsSelf()
            .AsSingleton();
    }
}
