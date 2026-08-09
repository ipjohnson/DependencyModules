# Attributes

Every attribute lives in `DependencyModules.Runtime.Attributes`.

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

### `[Intercept(params Type[])]`

Wraps a service so every call through its interface passes through the given interceptors, in order.

| Property | |
|---|---|
| `Service` | the interface to intercept, when the service implements more than one |
| `Order` | nesting relative to decorators and other interceptors |

## Environment conditions

All four take effect on a class that is registered by attribute or by convention. Conditions of
different kinds combine with **and**.

### `[IfEnvironment(params string[])]` · `[IfNotEnvironment(params string[])]`

Compares the environment name, case-insensitively.

### `[IfEnvironmentValue(key)]` · `[IfEnvironmentValue(key, value)]`

Presence of the key, or an exact ordinal match on its value. `AllowMultiple`.

### `[IfNotEnvironmentValue(…)]`

The inverse of either form.
