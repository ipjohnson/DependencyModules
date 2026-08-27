# Attributes

Every attribute this library defines, with its properties — for looking one up once you know what you
are after. If you are working out *which* attribute you want, the guide covers that:
[registering services](/guide/services), [modules](/guide/modules),
[decorators](/guide/decorators) and [environments](/guide/environments).

All of them live in `DependencyModules.Runtime.Attributes`.

## Modules

### `[DependencyModule]`

Marks a `partial` class as a module. Generates an attribute of the same name for composition.

| Property | |
|---|---|
| `OnlyRealm` | the module takes only registrations that named it as their realm |
| `GenerateAttribute` | set `false` to suppress the generated composition attribute |
| `RegisterJsonSerializers` | register discovered `JsonSerializerContext` types |

### `[Decorate(service, decorator)]`

Declares a decorator on the module rather than on the decorator class — for when the service, the
decorator or both come from an assembly you do not control.

| Property | |
|---|---|
| `Order` | nesting; lower sits closer to the implementation |

## Services

### `[SingletonService]` · `[ScopedService]` · `[TransientService]`

Registers the class, or a static factory method, with that lifetime.

| Property | |
|---|---|
| `As` | the service type to register as |
| `Key` | a service key |
| `Using` | `Add`, `Try`, `TryEnumerable` or `Replace` |
| `Realm` | scope the registration to one module |
| `Order` | where this registration sits among the others for the same service, lowest first |

`Order` decides the sequence an `IEnumerable<T>` dependency arrives in, which is what a pipeline of
validators or handlers reads. It decides a plain `GetService<T>()` too, since the container returns
the last registration — so the highest order wins that. Everything is `0` by default and the sort is
stable within one order, so naming an order for some services leaves the rest where they were.

### `[CrossWireService]`

Registers the implementation **and** every interface it declares, sharing one instance.

Takes the same properties, plus `Lifetime`.

## Decoration and interception

### `[Decorator]`

Marks a class as a decorator of the interface it implements. The first constructor parameter is the
wrapped instance; the rest are resolved from the container.

A `[Decorator]` is never a convention candidate — it is not a service.

| Property | |
|---|---|
| `Order` | nesting; lower sits closer to the implementation |
| `Service` | the decorated interface, when it cannot be inferred |
| `Realm` | restrict the decorator to one module, matching `Realm` on the service attributes |
| `Implementation` | decorate one implementation rather than every registration of the service |

An unrestricted decorator belongs to every module that is not `OnlyRealm`, exactly as an unrestricted
service registration does — so a decorator with no `Realm` is not picked up by an `OnlyRealm` module.

### `[Intercept(params Type[])]`

Wraps a service so every call through its interface passes through the given interceptors, in order.

| Property | |
|---|---|
| `Service` | the interface to intercept, when the service implements more than one |
| `Order` | nesting relative to decorators and other interceptors |
| `Realm` | restrict the interception to one module, matching `Realm` on the service attributes |
| `Lifetime` | how the interceptors are registered. `Singleton` by default |
| `Members` | which kinds of member to cover. Everything by default |

An interception applies to **the one implementation it was declared on**, not to every class behind
the interface — a sibling implementation carrying no `[Intercept]` is left alone. Decorators are the
other way round, and deliberately so.

With no `Realm` the interception takes the one its own class's service attribute names, so
`[SingletonService(Realm = typeof(X))]` and a plain `[Intercept]` agree without being told to. Naming
a realm explicitly still wins. An interception no module ends up applying is
[DM0020](/reference/diagnostics#dm0020).

`Members` takes `InterceptedMembers.Methods`, `.Properties`, `.Indexers`, `.Events` or any
combination. A member left out is still forwarded — the wrapper implements the whole interface either
way — it just does not run through the chain. See [Interception](/guide/interception).

## Environment conditions

All four take effect on a service registered by attribute or by convention, and on a
[`[Decorator]`](/guide/decorators#decorating-only-in-some-environments) — where a condition that does
not hold means the decorator is never applied. Conditions of different kinds combine with **and**.

The same tests are available on a [convention](/reference/conventions-api#environment-conditions)
itself, as `IfEnvironment(…)` and friends.

### `[IfEnvironment(params string[])]` · `[IfNotEnvironment(params string[])]`

Compares the environment name, case-insensitively.

### `[IfEnvironmentValue(key)]` · `[IfEnvironmentValue(key, value)]`

Presence of the key, or an exact ordinal match on its value. `AllowMultiple`.

### `[IfNotEnvironmentValue(…)]`

The inverse of either form.
