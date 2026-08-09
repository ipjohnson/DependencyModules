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

Implement `Conventions` **explicitly**, as above. An implicit `public void Conventions(…)` does not
compile.

::: tip Install
Conventions ship in their own analyzer package, so a project that does not use them never loads the
class-scanning providers.

```shell
dotnet add package DependencyModules.Conventions
```
:::

## The body never runs

`Conventions` is **read** at compile time, not executed. That means only the calls documented on this
page may appear in it — a loop, a conditional, a local variable or a call to your own helper is
reported as [DM0009](/reference/diagnostics#dm0009).

What comes out is ordinary registration code, one `services.AddScoped(…)` per match. Turn on
`EmitCompilerGeneratedFiles` to read it.

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

Turn it on for the common `CreateOrderValidator : AbstractValidator<CreateOrder>` shape. Bear in mind
that every future subclass of that base joins the convention too.

::: info Attributes always win
A type carrying `[SingletonService]`, `[ScopedService]`, `[TransientService]` or `[CrossWireService]`
is never a convention candidate. Neither is a `[Decorator]` — a decorator implements the interface
it decorates, and it is not a service.
:::

## Open generics

An open generic cannot be written as a type argument, so use the `Type` overload. Each match
registers against the **closed** construction it implements:

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

Prefer a service type, an attribute or a namespace where you can. A name pattern will happily match
a class somebody adds next year.

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

It requires a shape and at least one filter. Both are reported as
[DM0009](/reference/diagnostics#dm0009) if missing.

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
Interfaces in `System` or a namespace beginning `System.` are not expanded into, so a type whose base
implements `IDisposable` does not become resolvable as `IDisposable`.

This applies only to the expansion. A service type you name yourself is always honoured, so
`RegisterAll<IDisposable>()` still registers `IDisposable`.
:::

## Lifetime, keys and registration strategy

A lifetime is required; there is no default. Omitting one is
[DM0009](/reference/diagnostics#dm0009).

```csharp
conventions.RegisterAll<IRepository>()
    .AsScoped()
    .Using(RegistrationType.Try)     // Add, Try, TryEnumerable or Replace
    .WithKey("primary");             // literal, const or enum member
```

## When two conventions collide

Two conventions in one module registering the same implementation under the **same service type** is
[DM0004](/reference/diagnostics#dm0004), an error — the lifetime would be ambiguous.

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

Anything that would need a lambda over the matched types — a predicate, or a lifetime chosen per type
— cannot be expressed, because the declaration is read at compile time rather than run.

Use `IServiceCollectionConfiguration` for those, alongside your conventions:

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
