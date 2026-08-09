# Conventions

Attributes are explicit, and explicit stops being a virtue somewhere around the fortieth handler.
Conventions let a module say *what* to register once, and the generator works out which types fit
while it builds.

```csharp
[DependencyModule]
public partial class DataModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll<IRepository>().AsScoped();
        conventions.RegisterAll(typeof(IRequestHandler<,>)).AsTransient();
    }
}
```

::: tip Install
Conventions ship in their own analyzer package, so a project that does not use them never loads the
class-scanning providers.

```shell
dotnet add package DependencyModules.Conventions
```
:::

## The body never runs

This is the one thing worth understanding before anything else. `Conventions` is **read** at compile
time, not executed. The generator parses the calls out of your source, resolves the matching types,
and emits ordinary registrations. Nothing implements `IConventionDefinitions` at run time and the
method is never called.

That has two consequences.

**Only the declared calls may appear in it.** A loop, a conditional, a local variable or a call to
your own helper cannot be evaluated during a build, so they are reported as
[DM0009](/reference/diagnostics#dm0009) rather than quietly ignored.

**You get the same output as writing it by hand.** There is no convention engine at run time, no
registration strategy to configure. `EmitCompilerGeneratedFiles` will show you `services.AddScoped(…)`
calls, one per match.

## Why the interface name appears twice

```csharp
public partial class DataModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) { }
    // ^^^^^^^^^^^^^^^^^^ explicit implementation
}
```

`IConventionDefinitions` is emitted `internal` into your compilation, so an implicit
`public void Conventions(…)` is `CS0051: Inconsistent accessibility`. Explicit implementation is the
only shape that compiles.

Emitting the contract `internal` keeps it off your public API surface, and it means two assemblies
that both use conventions do not collide on the same type names.

## What matches

A type matches when it **declares** the service type, or declares an interface that extends it:

```csharp
public interface IAuditedRepository : IRepository { }

public class OrderRepository  : IRepository { }          // matches
public class AuditedOrders    : IAuditedRepository { }   // matches — IAuditedRepository extends IRepository
```

An interface saying it extends another is a deliberate statement that it is substitutable for it,
so it counts.

Reaching the service type through a **base class** does not, unless you ask:

```csharp
public abstract class RepositoryBase : IRepository { }
public class ProductRepository : RepositoryBase { }      // no match by default

conventions.RegisterAll<IRepository>().IncludeBaseClasses().AsScoped();   // now it matches
```

Extending a class is a statement about implementation reuse rather than about the contract, and
every subclass added years later would otherwise join the convention with nobody revisiting it. It
is an opt-in because the common `CreateOrderValidator : AbstractValidator<CreateOrder>` shape needs
it and because silently inheriting registration is worse than typing one call.

::: info Attributes always win
A type carrying `[SingletonService]`, `[ScopedService]`, `[TransientService]` or `[CrossWireService]`
is never a convention candidate. Neither is a `[Decorator]` — a decorator implements the interface
it decorates, and it is not a service.
:::

## Open generics

`typeof(IHandler<,>)` cannot be written as a type argument, which is why there is a `Type` overload.
Each match registers against the **closed** construction it actually implements:

```csharp
public class CreateOrderHandler : IRequestHandler<CreateOrder, OrderId> { }
public class RenameOrderHandler : IRequestHandler<RenameOrder, OrderId> { }

conventions.RegisterAll(typeof(IRequestHandler<,>)).AsTransient();
```

```csharp
// generated
services.AddTransient(typeof(IRequestHandler<CreateOrder, OrderId>), typeof(CreateOrderHandler));
services.AddTransient(typeof(IRequestHandler<RenameOrder, OrderId>), typeof(RenameOrderHandler));
```

A type implementing **several** closings registers against all of them:

```csharp
public class OrderEvents
    : INotificationHandler<OrderPlaced>, INotificationHandler<OrderShipped> { }
```

Both are registered. They are different service types, so this is not the same implementation
appearing twice.

A generic implementation that closes nothing registers as the open generic, and the container closes
it per request:

```csharp
public class PassThroughCache<T> : ICache<T> { }   // registers ICache<> itself
```

## Narrowing what matches

Filters chain, and combine with **and**. Alternatives go inside a single call.

```csharp
conventions.RegisterAll<IRepository>()
    .InNamespaceOf<OrderMarker>()          // and in this namespace or below it
    .WithoutName("*Legacy")                // and not named like this
    .WithAttribute<AuditedAttribute>()     // and carrying this attribute
    .AsScoped();
```

| Filter | Matches |
|---|---|
| `InNamespaceOf<TMarker>()` | the marker's namespace **and those beneath it** |
| `InNamespaces(params string[])` | the given namespaces and those beneath them |
| `InExactNamespaces(params string[])` | only those namespaces, not nested ones |
| `NotInNamespaceOf<TMarker>()`, `NotInNamespaces(…)` | excludes; applied after inclusions |
| `WithAttribute<T>()`, `WithoutAttribute<T>()` | the attribute type, resolved rather than name-matched |
| `WithName(params string[])`, `WithoutName(…)` | name globs — see below |

Namespace and name inclusions of the same kind combine with **or**; exclusions are applied afterwards
and any one of them removes a match.

### Name globs

Two wildcards and no regular expressions:

| Token | Matches |
|---|---|
| `*` | zero or more characters |
| `?` | exactly one character |

A pattern containing a dot is matched against the full `Namespace.TypeName`; otherwise against the
bare type name. Matching is ordinal and case-sensitive, like C# identifiers.

```csharp
conventions.RegisterAll<IRepository>().WithName("*Repository", "*Store").AsScoped();
```

Name globbing is the weakest selector here and is listed last deliberately. It is the one most likely
to match something nobody intended when a class is added years later — prefer a service type, an
attribute, or a namespace.

## Registering types that implement nothing

`RegisterAll()` with no service type selects by filter alone. It is how a concrete class that
implements no interface gets registered by convention:

```csharp
conventions.RegisterAll()
    .InNamespaceOf<OrderMarker>()
    .WithName("*Calculator")
    .AsSelf()
    .AsScoped();
```

It requires a shape — there is nothing to register the matches *as* otherwise — and at least one
filter. Without a filter it would match every class in the compilation, which is never what anybody
means, so it is reported rather than obeyed.

## What each match is registered as

| Call | Registers |
|---|---|
| *(default)* | the service type the convention matched |
| `AsSelf()` | the match's own concrete type, instead of the interface |
| `AlsoAsSelf()` | the matched service type **and** the concrete type, sharing one instance |
| `AsSelfWithInterfaces()` | the concrete type and **every** interface it implements, sharing one instance |
| `AsMatchingInterface()` | the interface named after the type — `Foo` as `IFoo` |
| `As<TService>()` | one named service type, whatever the match matched through |

### One instance or several

This is the distinction that catches people out with every scanning library.

```csharp
conventions.RegisterAll<IFoo>().AsSingleton();          // one registration
conventions.RegisterAll<IBar>().AsSingleton();          // another, same class
```

A class matched through two different interfaces gets **two registrations and two instances**, which
is what Scrutor and MediatR both produce and is usually what you want for handlers.

When you want one instance reachable through several service types, say so:

```csharp
conventions.RegisterAll(typeof(IValidator<>)).IncludeBaseClasses().AlsoAsSelf().AsScoped();
```

`AlsoAsSelf()` and `AsSelfWithInterfaces()` both cross-wire: resolving any of the registered service
types gives the same instance. The difference is reach — `AlsoAsSelf()` registers only the interfaces
the convention matched, `AsSelfWithInterfaces()` registers everything the type implements.

::: warning AsSelfWithInterfaces skips System interfaces
Interfaces declared in `System` or a namespace beginning `System.` are not expanded into. Without
that, any type whose base implements `IDisposable` would become resolvable *as* `IDisposable`, and a
FluentValidation validator would become resolvable as `IEnumerable<IValidationRule>`.

It applies only to the expansion. A service type you named yourself is always honoured, so
`RegisterAll<IDisposable>()` still registers `IDisposable`.
:::

## Lifetime, keys and registration strategy

A lifetime is required. There is no default, because a lifetime nobody wrote down is the most
expensive thing for a registration to get wrong — omitting one is
[DM0009](/reference/diagnostics#dm0009).

```csharp
conventions.RegisterAll<IRepository>()
    .AsScoped()
    .Using(RegistrationType.Try)     // Add, Try, TryEnumerable or Replace
    .WithKey("primary");             // literal, const or enum member
```

## When two conventions collide

Two conventions in one module that would register the same implementation under the **same service
type** is [DM0004](/reference/diagnostics#dm0004), an error. One lifetime has to win and the source
does not say which.

```csharp
conventions.RegisterAll<IRepository>().AsScoped();
conventions.RegisterAll<IRepository>().AsSingleton();   // DM0004
```

A type filling two *different* roles is not a collision, and registers as both:

```csharp
public class OrderEvents : INotificationHandler<OrderPlaced>, IRequestPreProcessor<ShipOrder> { }

conventions.RegisterAll(typeof(INotificationHandler<>)).AsTransient();
conventions.RegisterAll(typeof(IRequestPreProcessor<>)).AsTransient();   // fine
```

Conventions in *different* modules never collide — each registers into its own realm.

## What conventions will not do

Every Scrutor overload that takes a lambda — `Where(Func<Type,bool>)`,
`AsImplementedInterfaces(predicate)`, `WithLifetime(Func<Type,ServiceLifetime>)` — has no
compile-time equivalent. A generator cannot run your code over the types it is describing.

The escape hatch is a normal method, and it composes with everything above:

```csharp
[DependencyModule]
public partial class DataModule : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        // unrestricted access to IServiceCollection, at run time
    }
}
```

## Next

- [Scanning a package](/guide/scanning) — matching types in a referenced assembly
- [Convention API reference](/reference/conventions-api) — every call in one table
- [Diagnostics](/reference/diagnostics) — what each DM code means
