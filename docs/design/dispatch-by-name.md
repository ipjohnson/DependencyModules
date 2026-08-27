# Dispatch by name

Two of the four applications in the 1.1.0 field round hand-built the same thing: a dictionary from a
string to a handler, rebuilt on every request. Neither author knew the other was doing it. That is
the strongest signal in the round's feature table, and it is unaddressed in 1.2.0 because the answer
is a design choice rather than a missing property.

## What they were trying to write

A JSON-RPC server routes `"tools/call"` to a handler. A plugin host routes a step name to a
transform. In both cases the set of handlers is known at compile time, the key is a string on each
handler, and the lookup happens per request.

```csharp
// what agent 08 wrote, once per request
var handlers = provider.GetServices<IRequestHandler>()
    .ToDictionary(handler => handler.Method);

return handlers[request.Method].HandleAsync(request);
```

## Why the existing surface does not cover it

**Keyed services are the obvious answer and do not work here.** A keyed registration is reachable by
`GetRequiredKeyedService<T>(key)`, which is the right shape — but the keys have to come from
somewhere, and a convention applies one literal to every match:

```csharp
conventions.RegisterAll<IRequestHandler>().WithKey("handler").AsSingleton();  // one key, every match
```

There is no way to say "the key is a constant on each matched type". Writing them out by hand is the
thing conventions exist to avoid, and it puts the routing table somewhere other than the handler.

**And keyed registrations cannot be enumerated.** `GetServices<T>()` returns only the unkeyed ones,
so a host cannot ask "what can I route to?" — which it needs for a `tools/list` response, a
diagnostics page, or a startup check that two handlers have not claimed one name. Both applications
needed exactly that.

So the workaround is to register unkeyed, enumerate, and build the dictionary — paying a dictionary
build per request unless the author also knows to cache it, and losing the compile-time check that
would have caught a duplicate.

## The shape of an answer

Three candidates, in increasing order of what they ask of the library.

### A. A key derived per match

Let a convention compute the key from the matched type rather than take a literal:

```csharp
conventions.RegisterAll<IRequestHandler>()
    .WithKeyFrom(nameof(IRequestHandler.Method))
    .AsSingleton();
```

The generator would read a `const string` or a literal-returning property from each matched type and
emit one keyed registration per handler with that value.

*For:* small, fits the existing convention grammar, and the key stays on the handler.
*Against:* only works for a key the generator can read at compile time — a `const`, or a property
with a constant body. A key computed at runtime is out of reach, and the failure would have to be a
diagnostic rather than a fallback. It also does nothing about enumeration.

### B. Enumerable keyed services

Emit, alongside the keyed registrations, a registration of the map itself:

```csharp
public interface IKeyedRegistry<TService> : IReadOnlyDictionary<object, TService>;
```

`IKeyedRegistry<IRequestHandler>` is then injectable, built once, and answers both questions — route
to one, or list them all.

*For:* removes the per-request dictionary build, and makes the routing table a first-class thing a
diagnostics endpoint can read.
*Against:* new public surface, and a decision about whether the registry resolves eagerly (holding
every handler for the container's life) or lazily through the provider.

### C. Both

A is what puts the key on the handler; B is what makes the set usable. Each is useful alone, and
together they are the feature the two applications were reaching for.

## What has to be decided before this is written

1. **Where does the key come from?** A `const` on the type, an attribute on the type, or a lambda in
   the convention that the generator evaluates. Each has a different failure mode when the generator
   cannot read it, and each needs its own diagnostic.
2. **Does the registry resolve eagerly or lazily?** Eager is simpler and wrong for scoped handlers;
   lazy needs the provider and pushes lifetime questions into the registry.
3. **What happens when two handlers claim one key?** This is the check the hand-built dictionary
   silently lost — `ToDictionary` throws at first use, in a request rather than at build. A
   compile-time diagnostic is available here and is most of the value.
4. **Does this replace `WithKey` or sit beside it?** A literal key is still right for a small fixed
   set, so probably beside.

## Not doing it in 1.2.0

The release already carries three new attribute properties, three new diagnostics and a precedence
change. Dispatch-by-name is the largest item in the round's feature table and the one where guessing
the shape wrong is most expensive — it is public surface that cannot be taken back inside 1.x.

Recorded here rather than deferred silently, because two independent authors reaching for the same
missing thing is the clearest evidence the round produced.
