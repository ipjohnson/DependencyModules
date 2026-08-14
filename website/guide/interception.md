# Interception

## The problem

A [decorator](/guide/decorators) works well when you want to do something to one member. It scales
badly in two directions.

**Wide interfaces.** To time one method on an interface with twenty members, you write a decorator
with twenty methods — nineteen of which are pass-throughs that exist only to compile, and which
someone has to remember to update when a twenty-first member appears.

**Many services.** To time thirty unrelated services, you write thirty decorators. The behaviour is
identical in all of them; only the interface differs.

In both cases you are writing forwarding code by hand, and the actual logic is four lines.

## How DependencyModules helps

Write the behaviour once, as an interceptor. The generator emits a type implementing the service
interface and routes **every member** through it:

```csharp
public class TimingInterceptor(ILogger log) : IInterceptor {
    public TResult Intercept<TResult>(InvocationContext<TResult> context) {
        var stopwatch = Stopwatch.StartNew();

        try {
            return context.Proceed();
        } finally {
            log.LogInformation("{Member} took {Elapsed}", context.Caller.MemberName, stopwatch.Elapsed);
        }
    }
}
```

Apply it to any service, however many members it has:

```csharp
[SingletonService]
[Intercept(typeof(TimingInterceptor))]
public class Repository : IRepository { }
```

The return type comes from the generated call site rather than from reflection, so nothing is boxed
and nothing is inspected at run time.

::: info Only calls through the interface are intercepted
A call the implementation makes to *itself* does not pass through the wrapper — it is an ordinary
method call inside one object.
:::

## Three interfaces, chosen per member

A synchronous interceptor cannot serve a `Task`-returning member, because it has nowhere to await.
Implement whichever kinds your services actually have:

| Interface | For members returning |
|---|---|
| `IInterceptor` | a value directly, or `void` |
| `IAsyncInterceptor` | `Task`, `Task<T>`, `ValueTask`, `ValueTask<T>` |
| `IAsyncEnumerableInterceptor` | `IAsyncEnumerable<T>` |

One type may implement any combination, and **the generator picks per member**:

```csharp
public class TracingInterceptor : IInterceptor, IAsyncInterceptor {
    public TResult Intercept<TResult>(InvocationContext<TResult> context) => context.Proceed();

    public async ValueTask<TResult> InterceptAsync<TResult>(AsyncInvocationContext<TResult> context) {
        using var span = tracer.StartSpan(context.Caller.MemberName);

        return await context.ProceedAsync();
    }
}
```

A member that no interceptor can serve is forwarded untouched, with no allocation. So an interceptor
implementing only `IAsyncInterceptor`, applied to a service with both synchronous and asynchronous
members, intercepts the asynchronous ones and leaves the rest alone.

## Awaiting is yours

The generated wrapper awaits nothing on your behalf. Your interceptor awaits `ProceedAsync()` itself,
which means anything after the await runs once the work has genuinely finished — and because the
whole call sits in one method body, state that spans it is an ordinary local:

```csharp
public async ValueTask<TResult> InterceptAsync<TResult>(AsyncInvocationContext<TResult> context) {
    using var scope = _tracer.StartSpan(context.Caller.MemberName);   // spans the whole call

    return await context.ProceedAsync();
}
```

That `using` disposes after the awaited work completes, not when the `Task` was handed back.

## Streams

An `IAsyncEnumerable<T>` member returns its stream immediately, before any item exists. A stream
interceptor enumerates it, so it observes each item as it is produced:

```csharp
public async IAsyncEnumerable<TItem> InterceptStream<TItem>(StreamInvocationContext<TItem> context) {
    var count = 0;

    await foreach (var item in context.Proceed()) {
        count++;
        yield return item;
    }

    log.LogInformation("{Member} produced {Count}", context.Caller.MemberName, count);
}
```

## What the context gives you

| Member | |
|---|---|
| `Proceed()` / `ProceedAsync()` | run the rest of the pipeline — more than once to retry, or not at all to skip the implementation |
| `Caller.ServiceType`, `Caller.MemberName` | what is being called |
| `Arguments` | by index, and **writable** — a write replaces what the implementation receives. `NameAt(index)` gives the declared parameter name |

Arguments cost nothing until you read one.

## Several interceptors

```csharp
[Intercept(typeof(TimingInterceptor), typeof(RetryInterceptor))]
public class Repository : IRepository { }
```

They nest in declaration order. Each is resolved from the container, so an interceptor can take
dependencies of its own — as `TimingInterceptor` does with its `ILogger`.

## What cannot be intercepted

The generator has to emit a real override, so some shapes are impossible. These are reported as
[DM0008](/reference/diagnostics#dm0008) rather than failing the build:

- `ref`, `in` and `out` parameters, and `ref struct` parameters
- by-reference returns
- `init`-only setters
- static members
- a generic implementation whose type parameters are **constrained** — the wrapper would have to
  repeat the constraint, and there is no way to emit one. An unconstrained generic implementation is
  intercepted; see below.

::: warning One such member disables interception for the whole interface
There is no partial wrapper. A single `out` parameter anywhere on the interface means no wrapper is
generated at all, so every other member goes uninterceped too, and `GetRequiredService<IOrders>()`
returns the plain implementation. The diagnostic names the member it found first; fixing it may
uncover another.

Move the member to an interface that is not intercepted, or write a
[decorator](/guide/decorators) for the service instead.
:::

## Intercepting a generic service

A generic implementation registers as an open generic, and a decorator cannot touch one — decoration
rewrites a registration into a factory, and the container refuses a factory for an open generic
service type. Interception does not need a factory: the wrapper is a generated type, and an open
generic implementation type is what the container does accept.

```csharp
[SingletonService]
[Intercept(typeof(TracingInterceptor))]
public class Repository<T> : IRepository<T> { … }
```

The wrapper is generic over the same parameters — `Repository_Intercepted<T> : IRepository<T>` — and
takes `Repository<T>` by its own type rather than the service, which would resolve back to the wrapper
and recurse. The container closes it per construction, so `IRepository<Order>` and
`IRepository<Invoice>` each get their own.

::: warning Native AOT closes this over reference types only
An open generic registration is the container's least AOT-friendly shape, intercepted or not: a
published binary can construct `IRepository<Order>` and throws for `IRepository<int>`. That is not
specific to interception — a plain `[SingletonService]` on a generic class behaves identically. See
[Trimming and AOT](/guide/aot#what-it-does-not-cover).
:::

A **constrained** type parameter is refused, because the wrapper cannot repeat the constraint. Give
the service a closed construction to intercept instead.

## When an interceptor covers only some members

Separate from the above, and quieter. Each interceptor is placed only around the members whose shape
it can serve — `IInterceptor` for a direct return, `IAsyncInterceptor` for a task,
`IAsyncEnumerableInterceptor` for a stream — and it is simply absent from the rest:

```csharp
public class AuditInterceptor : IInterceptor { … }      // sync only

[SingletonService]
[Intercept(typeof(AuditInterceptor))]
public class Orders : IOrders {
    public int Count(string customer) { … }             // audited
    public Task<int> CountAsync(string customer) { … }  // not audited
}
```

That is [DM0015](/reference/diagnostics#dm0015). It is worth taking seriously rather than silencing:
an interceptor that rewrites arguments stops rewriting them, and one that authorises or audits stops
doing that — on the async members, which are usually the ones doing the work. Implement the missing
interface, or apply the interceptor to a service with no such member.

One type may implement any combination of the three, which is how a single interceptor covers a mixed
interface.
