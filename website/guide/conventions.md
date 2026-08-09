# Conventions

## The problem

Attributes are explicit, which is a virtue right up until you have forty of them saying the same
thing:

```csharp
[TransientService] public class CreateOrderHandler : IRequestHandler<CreateOrder, OrderId> { }
[TransientService] public class RenameOrderHandler : IRequestHandler<RenameOrder, OrderId> { }
[TransientService] public class ShipOrderHandler   : IRequestHandler<ShipOrder, Unit> { }
// … thirty-seven more
```

Nothing here is a decision. Every handler is transient because every handler is transient, and the
only real event is the day someone writes the forty-first and forgets the attribute. You are back to
the hand-maintained list, just spread across forty files instead of gathered in one.

## How DependencyModules helps

State the rule once, and let the generator find the types that fit **while it builds**:

```csharp
[DependencyModule]
public partial class DataModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll(typeof(IRequestHandler<,>)).AsTransient();
    }
}
```

Forty registrations, one declaration, and the forty-first handler registers itself by existing.

```shell
dotnet add package DependencyModules.Conventions
```

Conventions ship in their own analyzer package, so a project that does not use them never loads the
class-scanning providers.

::: warning Implement the interface explicitly
`void IConventionModule.Conventions(…)`, as above. An implicit `public void Conventions(…)` does not
compile.
:::

## The body never runs

This is the one genuinely surprising thing on this page, and everything else follows from it.

`Conventions` is **read at compile time, not executed**. The generator parses that method as source
and works out what you asked for. It is a declaration that happens to be written in C# syntax.

Two consequences:

**Only the calls documented on this page may appear in it.** A loop, an `if`, a local variable or a
call to your own helper method cannot be read, and is reported as
[DM0009](/reference/diagnostics#dm0009) rather than silently ignored.

**What comes out is ordinary registration code** — one `services.AddTransient(…)` per match, sitting
in your assembly. Turn on `EmitCompilerGeneratedFiles` and read it:

```csharp
// generated
services.AddTransient(typeof(IRequestHandler<CreateOrder, OrderId>), typeof(CreateOrderHandler));
services.AddTransient(typeof(IRequestHandler<RenameOrder, OrderId>), typeof(RenameOrderHandler));
```

## What matches

A type matches when it **declares** the service type, or declares an interface that extends it:

```csharp
public interface IAuditedRepository : IRepository { }

public class OrderRepository  : IRepository { }          // matches
public class AuditedOrders    : IAuditedRepository { }   // matches — IAuditedRepository extends IRepository
```

An interface declaring that it extends another is a deliberate statement that it is substitutable for
it, so it counts.

Reaching the service type through a **base class** does not count, unless you ask for it:

```csharp
public abstract class RepositoryBase : IRepository { }
public class ProductRepository : RepositoryBase { }      // no match by default

conventions.RegisterAll<IRepository>().IncludeBaseClasses().AsScoped();   // now it matches
```

Turn it on for the common `CreateOrderValidator : AbstractValidator<CreateOrder>` shape, where the
interface only ever arrives through a framework base class. Bear in mind that every future subclass
of that base joins the convention too.

::: info Attributes always win
A type carrying `[SingletonService]`, `[ScopedService]`, `[TransientService]` or `[CrossWireService]`
is never a convention candidate, so an attribute is how you exempt one type from a rule that would
otherwise catch it.

Neither is a `[Decorator]` — a decorator implements the interface it decorates, and it is not a
service in its own right.
:::

## Open generics

An open generic cannot be written as a type argument, so use the `Type` overload. Each match is
registered against the **closed** construction it actually implements:

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

A type implementing **several** closings is registered against all of them:

```csharp
public class OrderEvents
    : INotificationHandler<OrderPlaced>, INotificationHandler<OrderShipped> { }
```

Both are registered. They are different service types, so this is not one implementation registered
twice.

A generic implementation that closes nothing registers as the open generic, and the container closes
it per request:

```csharp
public class PassThroughCache<T> : ICache<T> { }   // registers ICache<> itself
```

## Narrowing what matches

A service type is often too broad on its own. Filters chain, and combine with **and**; alternatives
go inside a single call:

```csharp
conventions.RegisterAll<IRepository>()
    .InNamespaceOf<OrderMarker>()          // and: in this namespace or below it
    .WithoutName("*Legacy")                // and: not named like this
    .WithAttribute<AuditedAttribute>()     // and: carrying this attribute
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

Namespace and name inclusions of the same kind combine with **or**. Exclusions are applied afterwards,
and any one of them removes a match.

### Name globs

Two wildcards, and no regular expressions:

| Token | Matches |
|---|---|
| `*` | zero or more characters |
| `?` | exactly one character |

A pattern containing a dot is matched against the full `Namespace.TypeName`; otherwise against the
bare type name. Matching is ordinal and case-sensitive, like C# identifiers.

```csharp
conventions.RegisterAll<IRepository>().WithName("*Repository", "*Store").AsScoped();
```

Prefer a service type, an attribute or a namespace wherever you can. A name pattern will cheerfully
match a class somebody adds next year — and `*Handler` matches `LoggingHandler` too.

## Registering types that implement nothing

Some things worth registering implement no interface at all. `RegisterAll()` with no service type
selects by filter alone:

```csharp
conventions.RegisterAll()
    .InNamespaceOf<OrderMarker>()
    .WithName("*Calculator")
    .AsSelf()
    .AsScoped();
```

Because there is no interface to constrain it, this form **requires** a shape and at least one
filter. Missing either is [DM0009](/reference/diagnostics#dm0009).

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

This is the distinction that catches people out with every scanning library, so it is worth being
explicit about.

```csharp
conventions.RegisterAll<IFoo>().AsSingleton();          // one registration
conventions.RegisterAll<IBar>().AsSingleton();          // another, same class
```

A class matched through two different interfaces gets **two registrations and two instances**. That
is what Scrutor and MediatR both produce, and for handlers it is usually what you want.

When you want one instance reachable through several service types, say so:

```csharp
conventions.RegisterAll(typeof(IValidator<>)).IncludeBaseClasses().AlsoAsSelf().AsScoped();
```

`AlsoAsSelf()` and `AsSelfWithInterfaces()` both cross-wire — resolving any of the registered service
types gives the same instance. The difference is reach: `AlsoAsSelf()` registers only the interfaces
the convention matched, while `AsSelfWithInterfaces()` registers everything the type implements.

::: warning AsSelfWithInterfaces skips System interfaces
Interfaces in `System` or a namespace beginning `System.` are not expanded into, so a type whose base
implements `IDisposable` does not become resolvable as `IDisposable`.

This applies only to the automatic expansion. A service type you name yourself is always honoured, so
`RegisterAll<IDisposable>()` still registers `IDisposable`.
:::

## Lifetime, keys and registration strategy

A lifetime is **required**; there is no default. Omitting one is
[DM0009](/reference/diagnostics#dm0009) rather than a silent transient.

```csharp
conventions.RegisterAll<IRepository>()
    .AsScoped()
    .Using(RegistrationType.Try)     // Add, Try, TryEnumerable or Replace
    .WithKey("primary");             // literal, const or enum member
```

## When two conventions collide

Two conventions in one module registering the same implementation under the **same service type** is
[DM0004](/reference/diagnostics#dm0004), an error — the lifetime would be ambiguous:

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

Conventions in *different* modules never collide, because each registers into its own
[realm](/guide/modules#realms-keeping-a-registration-out-of-the-default-module).

## What conventions will not do

Anything needing a lambda over the matched types — a predicate, or a lifetime chosen per type —
cannot be expressed, because the declaration is read rather than run. There is no way to evaluate
your code at compile time.

Use `IServiceCollectionConfiguration` for those, alongside your conventions:

```csharp
[DependencyModule]
public partial class DataModule : IConventionModule, IServiceCollectionConfiguration {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll<IRepository>().AsScoped();
    }

    public void ConfigureServices(IServiceCollection services) {
        // unrestricted access to IServiceCollection, at run time
    }
}
```

## Next

- [Scanning a package](/guide/scanning) — matching types in an assembly you do not own
- [Convention API reference](/reference/conventions-api) — every call in one table
- [Diagnostics](/reference/diagnostics) — what each DM code means
