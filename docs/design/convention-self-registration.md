# Convention self-registration: the missing shape, and what `AsSelfWithInterfaces` should exclude

Status: designed, not implemented. Two independent changes, both in
`ConventionMatcher.BuildRegistrations`. Written as a work order.

Both come out of measuring convention registration against FluentValidation, which is the one library
of its kind that conventions can replace outright. (MediatR cannot be replaced and does not need to
be — `AddMediatR` registers its own handlers, so if you call it conventions add nothing, and if you
do not call it you do not have MediatR. There is no work item there.)

---

## 1. A shape between `Interfaces` and `SelfAndInterfaces`

FluentValidation registers every validator **twice** — as `IValidator<T>` and as the concrete type:

> Each found validator is registered both by its interface type and as itself.
> — `FluentValidation.DependencyInjectionExtensions/ServiceCollectionExtensions.cs`

Neither current shape produces that pair. Given the ordinary validator, and
`AbstractValidator<T> : IValidator<T>, IEnumerable<IValidationRule>` (verified against the
FluentValidation source):

```csharp
public class FooValidator : AbstractValidator<Foo> { }

conventions.RegisterAll(typeof(IValidator<>)).AsScoped().IncludeBaseClasses();
```

| shape | registers |
|---|---|
| default (`Interfaces`) | `IValidator<Foo>` — missing the concrete type |
| `AsSelf()` | `FooValidator` — missing the interface |
| `AsSelfWithInterfaces()` | `IValidator<Foo>`, `IValidator`, `IEnumerable<IValidationRule>`, `IEnumerable` — see item 2 |

The missing shape is **self plus the interfaces the convention matched**, rather than self plus every
interface the type can reach.

### The verb

`AlsoAsSelf()`, additive to the default rather than a replacement for it. It reads as what it does,
and it keeps `AsSelf` meaning "instead of" while `AlsoAsSelf` means "as well as".

```csharp
conventions.RegisterAll(typeof(IValidator<>)).AsScoped().IncludeBaseClasses().AlsoAsSelf();
```

Add it to `IConventionRegistration` in `ConventionContractSource`, a fourth member to
`ConventionRegisterAs` (`Models/ConventionModel.cs:47`), and an entry to
`ConventionModelUtility.RegisterAsCalls` (line 40). Note the existing conflict check at line 264
refuses two *different* `As*` calls in one chain, which is the right behaviour here too — `AsSelf()`
and `AlsoAsSelf()` together is a contradiction.

### Cross-wire it

Emit as `SelfAndInterfaces` already does: one registration per matched interface carrying
`CrossWire: true`, plus one of the implementation type. FluentValidation registers the pair
independently, which hands you **two instances per scope**; cross-wiring gives one. That is a
deliberate difference and the better behaviour — worth a line in the docs rather than matching FV
exactly.

### Watch `RegistrationKey`

`ConventionMatcher.cs:387` keys `(implementation, implementation)` for anything that is not
`Interfaces`. Under the new shape one match produces both interface registrations and a self
registration, so the key has to be computed per emitted registration, not per match, or two
conventions matching one type through different interfaces would collide on the self key.

The collision itself is correct when it happens — two conventions each registering `FooValidator` as
itself *is* a duplicate declaration and should be DM0004. Just make sure the interface registrations
are not dragged down with it.

### Tests

Behavioural, `GeneratedAssembly` style:

- a validator resolves as both `IValidator<Foo>` and `FooValidator`
- both resolve to the **same instance** within a scope, which is what distinguishes this from FV
- `AlsoAsSelf()` does not pull in interfaces the convention did not name
- `AsSelf()` and `AlsoAsSelf()` in one chain is refused

---

## 2. `AsSelfWithInterfaces` should exclude `System.*`

`BuildRegistrations` (`ConventionMatcher.cs:512-528`) loops `InterfacesInReach` and cross-wires
everything it finds. For the validator above that includes `IEnumerable<IValidationRule>` and
non-generic `IEnumerable`. Resolving `IEnumerable<IValidationRule>` and getting a validator is wrong,
and bare `IEnumerable` is meaningless as a service type.

This is not a validator problem. Any type whose base implements `IDisposable` gets `IDisposable`
cross-wired to it, which is the sharper version of the same bug.

### Prior art — both filter, both reactively, and they filter different things

| | excluded | not excluded |
|---|---|---|
| Autofac | `IDisposable` — `.Where(i => i != typeof(IDisposable))` in `GetImplementedInterfaces` | `IEnumerable` |
| Scrutor | `IEnumerable<T>`, `IEnumerable` — `ShouldRegister` | `IDisposable` |

Autofac filters the one that bites; Scrutor filters the exact pair that bites FluentValidation.
Neither filters both. These are patches applied after users hit them, and the list they imply keeps
growing: `IEquatable<T>`, `IComparable`, `ICloneable`, `ISerializable`.

### The rule

**Skip interfaces declared in `System` or a namespace beginning `System.`.** One rule instead of a
list that accretes, and it is defensible in a sentence: cross-wiring a BCL interface is never what
"register this as its interfaces" means.

It covers `IDisposable`, `IAsyncDisposable`, `IEnumerable`, `IEnumerable<T>`, `IEquatable<T>` and the
rest of that family at once. It keeps `IValidator<T>`, which is third-party and wanted.

**Do not extend it to `Microsoft.Extensions.*`.** `class Worker : BackgroundService` reaching
`IHostedService` is something a developer could legitimately want cross-wired, so that line is not
defensible the way the `System.*` one is.

### Scope of the filter

Only the *expansion* — the `SelfAndInterfaces` loop. It must **not** apply to the default shape,
where the registered interface is the one the convention named: `RegisterAll<IDisposable>()` is a
strange thing to write but the developer wrote it, and refusing to honour a named service type is a
different kind of wrong.

By the same reasoning it does not apply to item 1, whose interfaces are the matched ones.

### Two behaviours that already work and should stay working

- The `crossWired.Count == 0` fallback at line 523 registers the implementation type when nothing
  else survives. Filtering to zero therefore degrades to `AsSelf()`, which is the right outcome.
- DM0010 reports every convention registration at the class, so whatever survives the filter stays
  visible in the IDE. That is worth more than a longer blocklist — Scrutor and Autofac leak silently;
  here a leak is at least legible.

No diagnostic for a skipped interface. It would fire on ordinary code and say nothing actionable.

### Tests

- a type whose base implements `IDisposable` does not resolve as `IDisposable` under
  `AsSelfWithInterfaces()`
- a validator under `AsSelfWithInterfaces()` does not resolve as `IEnumerable<IValidationRule>`
- a third-party interface in a non-`System` namespace still registers
- `RegisterAll<IDisposable>()` with the default shape still registers `IDisposable`, proving the
  filter is scoped to the expansion
- a type reaching only `System.*` interfaces falls back to registering itself
