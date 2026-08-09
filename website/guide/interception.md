# Interception

An interceptor runs around every call to a service. Unlike a [decorator](/guide/decorators) you do
not write the wrapper — the generator emits a type implementing the service interface and routes
every member through your interceptor.

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

[SingletonService]
[Intercept(typeof(TimingInterceptor))]
public class Repository : IRepository { }
```

Because the interface is not generic and the method is, the return type comes from the generated
call site. Nothing is boxed and nothing is inspected at run time.

::: info Only calls through the interface
A call the implementation makes to itself does not pass through the wrapper.
:::

## Three interfaces, chosen per member

A synchronous interceptor has nowhere to await, so it cannot serve a `Task`-returning member — the
surrounding code would report completion when the task was handed back rather than when the work
finished. Implement whichever you can serve:

| Interface | For members returning |
|---|---|
| `IInterceptor` | a value directly, or `void` |
| `IAsyncInterceptor` | `Task`, `Task<T>`, `ValueTask`, `ValueTask<T>` |
| `IAsyncEnumerableInterceptor` | `IAsyncEnumerable<T>` |

A type may implement any combination. **The generator picks per member**, and a member no
interceptor can serve is forwarded untouched with no allocation.

```csharp
public class TracingInterceptor : IInterceptor, IAsyncInterceptor {
    public TResult Intercept<TResult>(InvocationContext<TResult> context) => context.Proceed();

    public async ValueTask<TResult> InterceptAsync<TResult>(AsyncInvocationContext<TResult> context) {
        using var span = tracer.StartSpan(context.Caller.MemberName);

        return await context.ProceedAsync();
    }
}
```

An interceptor implementing only `IAsyncInterceptor` applied to a service with both synchronous and
asynchronous members intercepts the asynchronous ones and passes the rest straight through. That is
deliberate: an interface is intercepted as a whole, and an interceptor has nothing to say about the
members it cannot serve.

## Awaiting is yours

The generated wrapper awaits nothing on your behalf. An interceptor awaits `ProceedAsync()` itself,
so anything after the await happens once the work has finished — and because the call is held in a
single method body, state spanning it is an ordinary local:

```csharp
public async ValueTask<TResult> InterceptAsync<TResult>(AsyncInvocationContext<TResult> context) {
    using var scope = _tracer.StartSpan(context.Caller.MemberName);   // spans the whole call

    return await context.ProceedAsync();
}
```

A `using` spanning the call is inexpressible with separate enter and exit hooks, which is why there
are none.

## Streams

An `IAsyncEnumerable<T>` member hands its stream back immediately, so wrapping it as an ordinary
value would measure the construction of the iterator and nothing else. A stream interceptor
enumerates it, and so observes each item as it is produced:

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
| `Arguments` | by index or by name, and **writable** — a write replaces what the implementation receives |

Arguments cost nothing until read, because they are typed fields that box only on access.

## Several interceptors

```csharp
[Intercept(typeof(TimingInterceptor), typeof(RetryInterceptor))]
public class Repository : IRepository { }
```

They nest in declaration order. Each is resolved from the container, so an interceptor may take its
own dependencies.

## What cannot be intercepted

The generator refuses rather than guessing, reporting [DM0008](/reference/diagnostics#dm0008):

- `ref`, `in` and `out` parameters, and `ref struct` parameters — they cannot live in a field, and
  async cannot take them either
- by-reference returns
- `init`-only setters
- static members
- generic implementations, which register as an open generic

Custom decorators remain the answer for those.
