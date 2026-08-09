# Convention API

Every call available inside a `Conventions` body. See [Conventions](/guide/conventions) for how they
fit together.

```csharp
[DependencyModule]
public partial class DataModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll<IRepository>().InNamespaceOf<Marker>().AsScoped();
    }
}
```

## Starting a convention

| Call | Selects |
|---|---|
| `RegisterAll<TService>()` | types assignable to `TService` |
| `RegisterAll(Type serviceType)` | the same, for an open generic — `typeof(IHandler<,>)` |
| `RegisterAll()` | nothing by assignability; requires a filter and a shape |

## Filters

Chained calls, combined with **and**. Inclusions of the same kind combine with **or**; exclusions are
applied afterwards and any one removes a match.

| Call | |
|---|---|
| `InNamespaceOf<TMarker>()` | the marker's namespace and those beneath it |
| `InNamespaces(params string[])` | those namespaces and those beneath them |
| `InExactNamespaces(params string[])` | only those namespaces |
| `NotInNamespaceOf<TMarker>()` | excludes that namespace and those beneath it |
| `NotInNamespaces(params string[])` | excludes those namespaces |
| `WithAttribute<TAttribute>()` | types carrying the attribute |
| `WithoutAttribute<TAttribute>()` | types not carrying it |
| `WithName(params string[])` | name globs — `*` and `?` |
| `WithoutName(params string[])` | excludes matching names |
| `IncludeBaseClasses()` | also match types reaching the service through a base class |
| `InAssemblyOf<TMarker>()` | scan the marker's assembly instead of this project |

## Shape

| Call | Registers each match as |
|---|---|
| *(default)* | the service type the convention matched |
| `AsSelf()` | its own concrete type, instead of the interface |
| `AlsoAsSelf()` | the matched service type **and** the concrete type, sharing one instance |
| `AsSelfWithInterfaces()` | the concrete type and every interface it implements, sharing one instance |
| `AsMatchingInterface()` | the interface named after it — `Foo` as `IFoo` |
| `As<TService>()` | one named service type |

## Lifetime and strategy

| Call | |
|---|---|
| `AsSingleton()` · `AsScoped()` · `AsTransient()` | required; there is no default |
| `Using(RegistrationType)` | `Add`, `Try`, `TryEnumerable` or `Replace` |
| `WithKey(object)` | a service key — literal, `const` or enum member |

## What is not offered

Anything taking a lambda — a predicate over types, or a lifetime chosen per type. The declaration is
read at compile time rather than run.

Use `IServiceCollectionConfiguration.ConfigureServices` for those.
