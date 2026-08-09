# DM0004: stop refusing types that fill more than one role

Status: **implemented**. Kept for the reasoning, which the code cannot carry — why a type filling two
roles is not an ambiguity, and why the diagnostic still fires when two conventions name one service
type. Written as a work order; what follows describes the change as it was planned, and all four
changes plus the test inversion landed as described.

Two notes for anyone reading it as a plan rather than as history:

- *Also in this file, and blocking the zero-warning gate* is resolved. All four CS8602s are cleared,
  including the one it deferred: a convention with no service type never reaches assignability
  matching.
- *Out of scope* — one convention matching a type through several closings of one open generic —
  was implemented immediately afterwards, as it predicted. `FirstMatchingInterface` is now
  `AllMatchingInterfaces`. Its prerequisite, change 2, is what made that a small change.

One thing the plan did not anticipate: keying the ambiguity check on `ConventionTypeKey` does not
work. That key is deliberately equal for every closing of one generic, so `IHandler<A>` and
`IHandler<B>` looked like one service type and a two-message handler was reported as a duplicate
declaration. The key is the type definitions themselves.

DM0004 currently refuses a type that two conventions in one module both match, and drops every
registration it would have produced. That is right when the conventions collide on one service type
and wrong when they reach the type through different interfaces — which is the ordinary shape of a
MediatR handler and the reason convention registration cannot cover MediatR today.

---

## The defect

`ConventionMatcher.RemoveAmbiguous` (`src/DependencyModules.Conventions/Utilities/ConventionMatcher.cs:277`)
groups matches by `match.Candidate.ImplementationType` alone (line 283). Any candidate appearing in
more than one group entry is reported and **dropped entirely** — nothing is added to `usable`
(lines 296-317).

So this registers nothing, and fails the build:

```csharp
public class OrderEvents : INotificationHandler<OrderPlaced>, IRequestPreProcessor<ShipOrder> { }

conventions.RegisterAll(typeof(INotificationHandler<>)).AsTransient();
conventions.RegisterAll(typeof(IRequestPreProcessor<>)).AsTransient();
```

Nothing about it is ambiguous. The two registrations name different service types, and MediatR's own
`ServiceRegistrar` registers exactly this shape against every handler interface the type closes.

## The rule

Group by **`(ImplementationType, Interface.InterfaceType)`**, not by `ImplementationType`.

- **Different interfaces → both register.** A type filling two roles is not an ambiguity; the two
  registrations are independently predictable from reading the module.
- **Same interface → still DM0004.** This is the case the diagnostic exists for: one service type
  claimed by two conventions, so one lifetime has to win and no one can tell which from the source.

  ```csharp
  conventions.RegisterAll<IRepository>().AsScoped();
  conventions.RegisterAll<IRepository>().AsSingleton();   // DM0004
  ```

Equal lifetimes on the same interface stay an error too. The outcome is predictable, but the
declaration is redundant, and silently collapsing a duplicate is the failure mode this codebase
avoids.

---

## Changes

### 1. Regroup `RemoveAmbiguous` — `ConventionMatcher.cs:277`

Key the dictionary on the pair. Everything else in the method stands: one match per key is usable,
more than one is reported and dropped. Keep insertion order — the emitted registration order feeds
the module snapshot tests.

### 2. One `ServiceModel` per implementation — `ConventionMatcher.cs:342`

`BuildServiceModels` emits one `ServiceModel` per match (line 354), so a type matched through two
interfaces now produces two models with the same `ImplementationType`. The attribute path does not
do that: `ServiceModelUtility` builds **one** `ServiceModel` per class carrying a list of
`ServiceRegistrationModel`s (`src/DependencyModules.SourceGenerator.Impl/Utilities/ServiceModelUtility.cs:140`).

Group the usable matches by `ImplementationType` and emit one model with N registrations. That
preserves the "indistinguishable from the attribute path" property `DependencyFileWriter` relies on,
and it is what keeps per-implementation state — constructor, conditions, cross-wire — from being
duplicated across models.

The constructor and conditions come from the candidate, which is shared across the matches, so
merging is a straight regroup with no reconciliation.

### 3. Rewrite the DM0004 message — `DependencyModuleDiagnostics.cs:63`

Current format names the two conventions' service types:

```
"'{0}' is matched by more than one convention in '{1}' — as '{2}' and as '{3}'."
```

Under the new key those are always the same string, so it would print `as 'IRepository' and as
'IRepository'` — the same confusing output the partial-class defect used to produce. Name the service
type once and put the difference in a trailing clause built by the caller:

```
"'{0}' is matched by two conventions in '{1}' that both register it as '{2}'. {3}"
```

with `{3}` either `They declare different lifetimes (Scoped and Singleton).` or `The declaration is
duplicated.` One descriptor, and neither wording repeats itself.

### 4. Update the release line — `AnalyzerReleases.Unshipped.md:11`

Reads `Two conventions in one module match the same type.` Should say they register it as the same
service type. DM0004 is unshipped, so the line is free to change.

---

## The test that has to be inverted

**`TwoConventionsMatchingOnePartialClassIsStillAmbiguous`** in
`tests/DependencyModules.Tests/GeneratorTests/ConventionRegistrationTests.cs` (uncommitted at the
time of writing) asserts DM0004 fires for:

```csharp
public partial class Thing : IFoo { }
public partial class Thing : IBar { }

conventions.RegisterAll<IFoo>().AsSingleton();
conventions.RegisterAll<IBar>().AsSingleton();
```

That is exactly the case this change legalises. The test was written to prove the partial-class merge
did not disable the ambiguity check, which is a real thing to hold onto — so replace the assertion
rather than deleting the test: `Thing` should register twice, once as `IFoo` and once as `IBar`, with
no DM0004. Then add a separate test that keeps the check honest by colliding on one interface.

New behavioural tests, in the `GeneratedAssembly` style the rest of the file uses — compile, load,
resolve, do not assert on generated text:

- a type matched by two conventions through different interfaces resolves as both
- both registrations point at the same implementation type
- two conventions naming the same service type still report DM0004 and register nothing
- same as above with equal lifetimes — still DM0004
- a type matched through different interfaces with different lifetimes registers both, each with its
  own lifetime (two instances is correct here, and is what Scrutor and MediatR both produce)

---

## Also in this file, and blocking the zero-warning gate

`ConventionMatcher` carries four CS8602s introduced by the nullable `ConventionModel.ServiceType` in
the current working tree:

| line | expression |
|---|---|
| 70 | `convention.ServiceType.Name` — use `convention.DisplayName` |
| 147 | `convention.ServiceType.Equals(candidateInterface.InterfaceType)` in `FirstMatchingInterface` |
| 308 | `first.Convention.ServiceType.Name`, `second.Convention.ServiceType.Name` |

Line 308 disappears with change 1. Lines 70 and 147 are on the path this work touches and the build
gate is 0 warnings, so clear them here. Line 147 needs a decision that belongs to the in-flight
`RegisterAll()` work rather than to this one: a convention with no service type selects by filter, so
it should not reach assignability matching at all.

---

## Out of scope, and why it is worth knowing

**This does not fix one convention matching a type through several closings of one open generic.**

```csharp
public class OrderEvents : INotificationHandler<OrderPlaced>, INotificationHandler<OrderShipped> { }
```

`FirstMatchingInterface` (`ConventionMatcher.cs:138`) returns the first match per candidate per
convention, so this registers `INotificationHandler<OrderPlaced>` only, silently. That is parity step
4 and a separate change — `FirstMatchingInterface` becomes `AllMatchingInterfaces` and returns a list.

It is worth doing next, and change 2 is its prerequisite: once one `ServiceModel` carries N
registrations, returning several interfaces per convention needs no further plumbing. Both changes
are needed before conventions can honestly claim to cover MediatR; neither alone is enough.
