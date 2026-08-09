# Decorators

A decorator wraps a registered service with a type you write. You get real signatures, real
parameter names, and no generics gymnastics — which is what makes it the right tool when you want to
do something specific to one member.

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

Resolving `IRepository` now gives you `CachingRepository` wrapping `SqlRepository`.

## How it is wired

The **first constructor parameter is the wrapped instance**; every other parameter is resolved from
the container. You never register the decorator yourself — `[Decorator]` is enough, and the
decorator is not registered as a service in its own right.

That last part matters with [conventions](/guide/conventions): a decorator implements the interface
it decorates, so a convention scanning that interface would otherwise match the decorator too.
`[Decorator]` takes it out of convention matching for exactly that reason.

## Ordering

Decorators are sorted across **every module** in an `AddModule(s)` call, not just within the module
that declared them. Lower orders sit closer to the implementation; higher ones wrap them.

```csharp
[Decorator(Order = 10)] public class Retrying(IRepository inner) : IRepository { }
[Decorator(Order = 20)] public class Logging(IRepository inner)  : IRepository { }

// resolves as Logging(Retrying(SqlRepository))
```

By convention framework packages use 0–999 and application code 1000 and above, so an application's
decorators wrap those contributed by the libraries it consumes.

Two decorators of one service sharing an order is [DM0007](/reference/diagnostics#dm0007) — the
nesting would be unpredictable from reading the source.

## Open generics

One decorator can wrap every closed registration of an open generic. This is the shape that makes
cross-cutting behaviour over MediatR handlers or FluentValidation validators a single declaration:

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

Combined with a convention, that is the whole setup:

```csharp
conventions.RegisterAll(typeof(IRequestHandler<,>)).AsScoped();
```

Every handler is registered and every handler is wrapped.

## Decorating from the module

When the service, the decorator, or both come from an assembly you do not control, there is nowhere
to put `[Decorator]`. Declare it on the module instead:

```csharp
[DependencyModule]
[Decorate(typeof(IRepository), typeof(CachingRepository), Order = 100)]
public partial class DataModule;
```

## Ordering relative to services

Decoration runs as a distinct phase **after** every module's registrations, so a decorator sees
everything registered by every module in the call. You do not have to sequence anything, which is
the main difference from `Decorate()` in Scrutor.

The contract is worth stating precisely: a decorator sees the services registered by the modules in
its `AddModule(s)` call. Anything the application registers afterwards is outside that scope, which
is inherent to `IServiceCollection` — decoration rewrites descriptors, so it can only see
descriptors that exist.

## One limitation

A service **registered as an open generic** — a single generic implementation serving every closing
— cannot be decorated:

```csharp
[SingletonService]
public class Repository<T> : IRepository<T> { }   // registers IRepository<> itself

[Decorator]
public class CachingRepository<T>(IRepository<T> inner) : IRepository<T> { }
```

```
InvalidOperationException: 'IRepository`1' is registered as an open generic and cannot be
decorated by 'CachingRepository`1'. …
```

Decoration replaces a registration with a factory, and the container does not allow a factory for an
open generic service type. Register closed constructions instead.

Note this is about the *registration*, not the decorator. An open generic decorator over closed
registrations — the example further up — works and is the common case.

## Decorator or interceptor?

|  | Decorator | [Interception](/guide/interception) |
|---|---|---|
| Who writes the wrapper | you | the generator, every member |
| Applies to | one interface | many unrelated services |
| Member access | real signatures | uniform, `TResult` and `IArguments` |
| Reach for it when | caching *this* method, validating *that* one | logging, timing, retry, tracing |
