# Decorators

## The problem

You want to cache the results of a repository:

```csharp
[SingletonService]
public class SqlRepository : IRepository {
    public Item Get(int id) => /* a database round trip */;
}
```

Putting the cache inside `SqlRepository` gives that class a second job and makes it harder to test.
Putting it in every caller is worse. What you want is something that sits **between** the callers and
the repository, without either side knowing.

Microsoft's container has no built-in way to express that.

## How DependencyModules helps

Write the wrapper as an ordinary class, mark it `[Decorator]`, and it takes over the registration:

```csharp
public interface IRepository { Item Get(int id); }

[SingletonService]
public class SqlRepository : IRepository {
    public Item Get(int id) => /* … */;
}

[Decorator]
public class CachingRepository(IRepository inner, IMemoryCache cache) : IRepository {
    public Item Get(int id) => cache.GetOrCreate(id, _ => inner.Get(id))!;
}
```

Resolving `IRepository` now gives you `CachingRepository` wrapping `SqlRepository`. Neither the
callers nor `SqlRepository` changed.

## How it is wired

The **first constructor parameter is the wrapped instance**; every other parameter is resolved from
the container normally. That is the whole convention.

You never register the decorator yourself — `[Decorator]` is enough, and the decorator is not
registered as a service in its own right. This also keeps it out of
[convention](/guide/conventions) matching, which matters because a decorator implements the very
interface a convention over that interface would be looking for.

## Ordering

With more than one decorator, `Order` decides the nesting. **Lower orders sit closer to the
implementation**; higher ones wrap them:

```csharp
[Decorator(Order = 10)] public class Retrying(IRepository inner) : IRepository { }
[Decorator(Order = 20)] public class Logging(IRepository inner)  : IRepository { }

// resolves as Logging(Retrying(SqlRepository))
```

So a logged call reports the whole retry sequence as one operation, which is usually what you want.

Ordering is global — decorators are sorted across **every module** in an `AddModule(s)` call, not
just within the module that declared them. By convention framework packages use 0–999 and application
code 1000 and above, so an application's decorators wrap the ones contributed by libraries it
consumes.

Two decorators of one service sharing an order is [DM0007](/reference/diagnostics#dm0007), since
their nesting would be ambiguous.

## One decorator over every closed generic

This is where decorators earn their keep. A single declaration can wrap **every** closed registration
of an open generic — cross-cutting behaviour over all your MediatR handlers or FluentValidation
validators, written once:

```csharp
[Decorator]
public class LoggingHandler<TRequest, TResponse>(
    IRequestHandler<TRequest, TResponse> inner, ILogger log)
    : IRequestHandler<TRequest, TResponse> {

    public TResponse Handle(TRequest request) {
        log.LogInformation("handling {Request}", typeof(TRequest).Name);
        return inner.Handle(request);
    }
}
```

Combined with a convention, that is the entire setup:

```csharp
conventions.RegisterAll(typeof(IRequestHandler<,>)).AsScoped();
```

Every handler registered, every handler wrapped, and a new handler joins both by existing.

## Decorating only in some environments

A decorator carries [environment conditions](/guide/environments) the same way a service does, which
is how you get behaviour that exists only where you want it — request logging in development, a
circuit breaker only in production:

```csharp
[Decorator]
[IfEnvironment("Development")]
public class LoggingRepository(IRepository inner, ILogger log) : IRepository {
    public Item Get(int id) {
        log.LogInformation("getting {Id}", id);
        return inner.Get(id);
    }
}
```

Outside Development the decorator is **never applied**, so `IRepository` resolves as the undecorated
implementation. Nothing wraps it and nothing tests the environment per call — the decision is made
once, while the modules are being applied.

All four condition attributes work, and they combine with **and** exactly as they do on a service:

```csharp
[Decorator]
[IfNotEnvironment("Production")]
[IfEnvironmentValue("TRACE_SQL", "on")]
public class TracingRepository(IRepository inner) : IRepository { … }
```

A condition changes **whether** a decorator applies, never **where it sits**. Ordering is unaffected,
so a conditional decorator dropping out leaves the rest of the chain nesting exactly as before:

```csharp
[Decorator(Order = 10)] [IfEnvironment("Development")] public class Inner(IRepository r) : IRepository { }
[Decorator(Order = 20)]                                public class Outer(IRepository r) : IRepository { }

// Development: Outer(Inner(SqlRepository))
// Production:  Outer(SqlRepository)
```

## Decorating a type you do not own

When the service, the decorator, or both come from an assembly you do not control, there is nowhere
to put `[Decorator]`. Declare it on the module instead:

```csharp
[DependencyModule]
[Decorate(typeof(IRepository), typeof(CachingRepository), Order = 100)]
public partial class DataModule;
```

## When decoration happens

Decoration runs as a distinct phase **after** every module's registrations, so a decorator sees
everything registered by every module in the call, regardless of the order they were added in. You do
not have to sequence anything.

The boundary is the `AddModule(s)` call: anything you register afterwards is outside that scope and
will not be decorated.

## One limitation

A service **registered as an open generic** — one generic implementation serving every closing —
cannot be decorated:

```csharp
[SingletonService]
public class Repository<T> : IRepository<T> { }   // registers IRepository<> itself

[Decorator]
public class CachingRepository<T>(IRepository<T> inner) : IRepository<T> { }
```

This is [DM0013](/reference/diagnostics#dm0013) at build time, whichever way the decorator was
declared — on the class, or on the module with `[Decorate]`.

Register closed constructions instead. A [convention](/guide/conventions) over the open generic
registers one per implementation, and an open generic decorator is then expanded across them.

Note that this is about the **registration**, not the decorator. An open generic decorator over
closed registrations — the example further up — works, and is the common case.

## Decorator or interceptor?

|  | Decorator | [Interception](/guide/interception) |
|---|---|---|
| Who writes the wrapper | you | the generator, for every member |
| Applies to | one interface | many unrelated services |
| Member access | real signatures and parameter names | uniform, `TResult` and `IArguments` |
| Reach for it when | caching *this* method, validating *that* one | logging, timing, retry, tracing |

If you need to do something specific to one member, write a decorator. If you need to do the same
thing to every member of thirty services, read on.
