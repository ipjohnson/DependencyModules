# Change plan: a reflection-free runtime and a single generator

Status: plan, being executed. The evidence behind every claim here is in
`docs/design/aot-decorators-and-convention-cost.md`; this document does not repeat it, it decides
what to do about it.

Two decisions drive everything below.

**The runtime does no reflection.** Not gated behind a feature switch, not opt-out — absent. Type
closure, `ActivatorUtilities`, interface walks: gone. What a decorated registration costs must be
what an undecorated one costs, on the same construction path.

**One analyzer assembly.** `DependencyModules.Conventions` is retired and the convention contracts
move into `DependencyModules.Runtime`.

---

## Assessment of the merge, before committing to it

Asked for thoughts, so: the merge is right, and for a reason stronger than the one that motivated it.

### It is what makes decorator monomorphisation uniform

The investigation split generic decoration into two cases. Case A — attribute-registered services —
is already one assembly and monomorphises with no new machinery. Case B — convention-registered
services — is the one that needed either a package merge or a cross-package dedup protocol.

**The merge deletes case B rather than solving it.** Convention registrations and decorators end up
in one compilation stage, and monomorphisation becomes one code path instead of two with a
reconciliation problem between them. Everything in Part 6 of the investigation about "emit from both
packages with a guard" stops being a question.

That is worth more than the packaging tidiness, and it was not the stated motivation.

### It retires a documented wart rather than trading it

`convention-registration-and-decorators.md` records why the contracts are emitted `internal` per
assembly, and the two costs it accepted: explicit interface implementation
(`void IConventionModule.Conventions(…)`), and CS0436 when two assemblies that both emit the
contracts reference each other — measured, three warnings.

The objection it raised against making them public was *"they join the consumer's public API
surface."* **That objection is specific to emitting them into the consumer and does not survive the
move.** In `DependencyModules.Runtime` they are Runtime's public API, not yours, exactly like
`IDependencyModule`. So:

- `public void Conventions(IConventionDefinitions conventions)` becomes legal. The explicit
  implementation is no longer forced.
- CS0436 cannot occur — there is one definition.
- The 413-line post-initialization source disappears from every compilation. It is the only
  `RegisterPostInitializationOutput` in the repository, so after this there are none.

### It removes duplicated compilation

`DependencyModules.Conventions` compiles in every `Impl` source, so today both analyzer assemblies
declare every `Impl` type. Anything referencing both sees genuine CS0433 duplicate-type errors — hit
while building the measurement harness for the investigation, and worked around with an extern alias.
One assembly ends that.

### Three things that need deciding, not assuming

**1. Version coupling becomes real.** Today the generator emits the contract, so the fluent API and
the generator that reads it cannot disagree. Moved to Runtime, a consumer on Runtime 1.0 with
generator 1.1 gets a compile error in their own code the moment they use a new verb. That is a loud,
correct failure and is acceptable — but it should be deliberate. Keep `KnownTypes` the single source
of truth for the names, and add a test asserting the Runtime interface and the generator's
expectations agree, so a rename cannot silently stop matching.

**2. The performance risk is real and must be paid down first, not promised.** Merging without the
transform work makes every project pay the convention scan: measured, **+10 ms per keystroke at 2,000
classes**, whether or not a single convention is declared. That is the whole reason the package
boundary existed.

So the merge is **gated on a measured number**, not on intent:

> The candidate provider must cost **under 2 ms incremental at 2,000 classes with zero matches**,
> measured by `benchmarks/DependencyModules.Benchmarks`, before the packages merge.

The floor for a syntax-only transform measured 1.7 ms, so the budget is reachable but not free. If it
is not met, the merge waits.

**3. The package deletion is a hard break.** Anyone referencing `DependencyModules.Conventions` gets
an unresolvable reference. At `1.0.0-rc` that is affordable. Ship a transitional empty package that
depends on `DependencyModules.SourceGenerator` so the failure is "this package is now empty, remove
it" rather than "package not found" — it costs one `.csproj` and one release note.

### One thing to measure early, because it may make this much cheaper

The investigation measured that the candidate transform re-runs for **every** candidate on **every**
driver run. It also measured that with no post-initialization output, a run where nothing changed at
all did not re-run the transform, while with post-initialization output it did.

Removing the contract source removes the only post-initialization output in the repository.
**Whether that restores transform caching is the single highest-leverage unknown in this plan**, and
it is one benchmark run to answer. Do it before writing the caching layer — the answer decides
whether the `ConditionalWeakTable` in step 5 is necessary or redundant.

---

## The change

Ordered so each step ships on its own, and so the binary-breaking ones land before 1.0.

### Step 1 — make the rule mechanical

`DependencyModules.Runtime.csproj`:

```xml
<IsAotCompatible>true</IsAotCompatible>
<WarningsAsErrors>$(WarningsAsErrors);IL2026;IL2055;IL2067;IL2072;IL2075;IL2087;IL3050</WarningsAsErrors>
```

Nothing in the investigation would have shipped had this been on. It is what turns "no reflection"
from a policy into a build failure, and it must go first so every later step is checked by the build
rather than by review.

This will fail immediately on the five call sites in `DecoratorHelper` and the missing annotation in
`DependencyRegistry`. That is the point; step 2 clears it.

### Step 2 — rewrite `DecoratorHelper` with no reflection

Split in two, because the second half changes a public API and the first half does not.

**2a — displacement, no API change. Done.** `CreateInner`/`CreateKeyedInner` are replaced by
`CaptureInner`, which resolves the descriptor shape once at decoration time and displaces an
implementation type under `DisplacedImplementationKey`. Both public overloads are unchanged, so the
generator needed no edit. The two failing tests pass, 571 unit and 133 integration tests stay green,
and the four `DecoratorHelper` IL warnings drop to the three that belong to the type-driven overload.

Tests added with it: the displaced registration keeps the original lifetime, the implementation does
**not** become resolvable through the container, and stacking displaces once rather than once per
decorator.

**Discovered while doing it, and it needs a decision:** displacement resolves through
`GetRequiredKeyedService`, so decoration now requires an `IKeyedServiceProvider`. Microsoft's
provider is one; a third-party container adapting `IServiceCollection` may not be. The alternative —
re-registering the implementation as its own service type — works on any provider but makes
`GetService<Foo>()` start answering where it previously returned null, which is a surface change and
is covered by a new test asserting it does not happen. Keyed is the better default and the library
already emits keyed registrations elsewhere, but the constraint should be documented rather than
discovered.

**2b — the generic overload. Done.** `Decorate<TService>(services, Func<IServiceProvider, TService,
TService>)` reuses the displacement core. The service being a type argument means `typeof(IRepo<>)`
cannot be written at the call site, so the open-generic mistake is inexpressible rather than detected
and thrown about — `GuardOpenGenericRegistration` becomes unreachable from generated code.

### Step 4 — emit closed decorations. Partly done, and the AOT result is in.

`DecoratorFileWriter` now emits, for a non-generic `[Decorator]`:

```csharp
DecoratorHelper.Decorate<global::App.IGreeter>(services,
    (provider, inner) => new global::App.ShoutingGreeter(inner, provider.GetRequiredService<global::App.ICache>()));
```

**Verified by publishing Native AOT and running it.** The non-generic decorator that failed in Part 1
of the investigation with *"a suitable constructor for type 'ShoutingGreeter' could not be located"*
now resolves:

```
control: NON-generic decorator (IGreeter):
  resolved as ShoutingGreeter
  result HELLO

all resolved
```

**And the publish reports zero IL warnings, down from four.** That is worth drawing out, because it
changes a decision made in Part 8 of the investigation. The reflective overload still exists on
`DecoratorHelper`, but nothing in that application calls it any more, so ILC never roots it and
`MakeGenericType` is trimmed along with its IL3050. **The feature switch proposed for turning the
fallback off is unnecessary**: emitting no reflective call is sufficient, and the trimmer does the
rest. The overload can simply be deleted once nothing emits it.

**Generic decorators are monomorphised too.** `DecoratorSourceGenerator` no longer derives from
`BaseAttributeSourceGenerator<DecoratorModel>`; it composes two providers, its own attributes and the
service attributes, and expands each generic decorator into one closed decoration per registration
that closes it. `AttributeModelCollector` holds the provider-building both share, and
`DecoratorTypeUtility.Close` does the substitution — including constructor parameters, so a decorator
taking `IValidator<TReq>` resolves `IValidator<CreateOrder>`.

**The whole thing, published Native AOT and run, with every decoration emitted by the generator:**

```
reference-type response (IRequestHandler<CreateOrder, OrderId>):
  resolved as LoggingHandler`2   ->  OrderId { Value = abc }

value-type response (IRequestHandler<CountRequest, int>):
  resolved as LoggingHandler`2   ->  42

control: NON-generic decorator (IGreeter):
  resolved as ShoutingGreeter    ->  HELLO

all resolved                                                    0 IL warnings
```

That is the defect this whole investigation started from, closed. The value-type instantiation is the
one Native AOT could never produce at run time, and it is now written into the assembly.

Three cases still take the reflective path, each reported by **DM0013** naming the decorator and the
reason, so none of them is silent:

- **Module-level `[Decorate(typeof(IFoo), typeof(FooDecorator))]`**, which names the decorator by
  `typeof()` and so carries no constructor. Reading one needs a symbol lookup this path does not
  have — and the decorator may be in another assembly, which is Part 8's problem in miniature.
- **A generic decorator no registration in this compilation closes** — including the case that
  matters most in practice, handlers registered by *convention*, whose models live in the other
  analyzer assembly. This is case B, and the merge is what closes it.
- **A shape whose type parameters are not the service's arguments in order**, which cannot be closed
  by position.

`HasUnboundServiceType` exists because of a bug this found: a generic decorator that expanded to
nothing fell through to the closed path and emitted `Decorate<IHandler<>>`, which is CS7003 in
generated code — the failure mode this generator is built never to produce. Caught by
`ConventionDecoratorTests`, which is exactly the case that reaches it.

`TypeParametersMatchService` was added deliberately rather than assumed:
`Logging<TRes, TReq> : IHandler<TReq, TRes>` is legal C# that cannot be monomorphised by position,
and guessing would emit a `new` with the arguments swapped — which *compiles* whenever the two types
are compatible. It is refused instead.

**New surface:**

```csharp
public static void Decorate<TService>(
    IServiceCollection services,
    Func<IServiceProvider, TService, TService> factory) where TService : class;
```

**Deleted:** `Decorate(IServiceCollection, Type, Type)`, `ResolveTypeArguments`, `Matches`, and the
`Type`-keyed `Decorate` overload. `GuardOpenGenericRegistration` goes with them — `typeof(IRepo<>)`
cannot be written as a type argument, so generated code can no longer express the mistake. Where a
registration is *made* as an open generic and a decorator targets it, the generator can see that at
compile time and reports it as a diagnostic instead.

**Inner production**, the only genuinely hard part:

| descriptor shape | before | after |
|---|---|---|
| `ImplementationInstance` | returned | unchanged |
| `ImplementationFactory` | invoked | unchanged |
| `ImplementationType` | `ActivatorUtilities.CreateInstance` | **displaced under a private key; the container builds it** |

```csharp
services.Add(new ServiceDescriptor(implementationType, innerKey, implementationType, lifetime));
services[i] = new ServiceDescriptor(typeof(TService),
    p => factory(p, (TService)p.GetRequiredKeyedService(implementationType, innerKey)), lifetime);
```

Verified: zero IL warnings under `IsAotCompatible`, works under Native AOT for reference-type and
value-type type arguments, and fixes the disposal leak.

**Be accurate about the claim.** A container cannot construct a type named by `Type` without
reflecting; `AddSingleton<IFoo, Foo>()` reflects. The line being defended is *DependencyModules adds
no reflection of its own — a decorated registration is constructed by exactly the same path as an
undecorated one.* Say that in the XML docs, not "zero reflection".

**Details that will bite if skipped:**

- The private key must be **deterministic** — derived from the service type and decorator identity,
  never a counter. A counter is not thread-safe and makes the collection differ between runs.
- Stacked decorators need no extra displacement: after the first rewrite the descriptor is a factory,
  so only the innermost is ever displaced. Assert this in a test.
- Keyed registrations compose the original key into the private key.
- `services.Count` grows by one per decorated implementation-type registration. Existing tests
  asserting on counts need updating; that is expected, not a regression.

### Step 3 — annotate `DependencyRegistry`. Done.

`[DynamicallyAccessedMembers(PublicConstructors)]` on `Add<TInstance>(Type implementationType, …)`.
Pure annotation, no behaviour, one IL2067 gone. It changes the public API signature, so
`PublicApiTests.RuntimeApi` caught it and the snapshot was updated — which is the snapshot doing its
job, not an obstacle.

**Step 1 stays off until 2b lands.** Three IL warnings remain, all of them in the type-driven
overload — `MakeGenericType`, `ActivatorUtilities`, and the `GetInterfaces` walk that only exists to
feed the first. Turning on `WarningsAsErrors` now would break the build for the duration of the work
rather than at the end of it.

### Step 4 — emit closed decorations

`DecoratorFileWriter` emits one `Decorate<TClosedService>(services, (p, inner) => new …)` per closed
registration rather than one open-generic call per decorator. `ConstructorInfoModel` is already
computed, so the decorator's remaining constructor parameters are already known.

Add the dedup guard: refuse to apply the same decorator to a descriptor twice, keyed on the
decorator's generic type definition. Two conventions or a convention and an attribute can put two
implementations behind one closed service, and both emissions would otherwise wrap both.

Where the generator cannot see a registration — hand-written `services.Add…` in `Program.cs` — it is
**not decorated**, and there is no fallback. That is a narrowing of the contract and belongs in the
documentation and in `AnalyzerReleases`, not in a footnote.

### Defect found by the new tests, and fixed

`[Decorate(typeof(IHandler<>), typeof(LoudHandler<>))]` — a **generic** decorator named by a
module-level attribute — does not compile. The attribute is re-emitted onto the generated module
partial with the unbound type parameter intact, producing `CS0246: the type or namespace name 'T'
could not be found` in `{Module}.Module.g.cs`.

Two causes, one in each half.

`typeof(IHandler<>)` binds to the **unbound** symbol, whose `TypeArguments` are the declaration's
type *parameters*. Re-emitting that verbatim writes `typeof(IHandler<T>)` into the generated module,
where `T` is not in scope. `AttributeModelHelper` now blanks the arguments when the syntax was
written as `Foo<>` — which fixes it for every module attribute carrying an unbound generic
`typeof`, not only `[Decorate]`.

Underneath that, the decoration was then dropped: a generic decorator declares its parameter as
`IHandler<T>` while the attribute named `IHandler<>`, and those never compare equal, so no
constructor parameter looked like the service. `ModuleDecoratorResolver` now compares on the unbound
form of both, while keeping the parameter type's names — closing the decorator over a registration
reads the type parameter order back off it.

`ModuleLevelDecorate_CanNameAGenericDecorator` covers both.

### Step 4b — the three cases still on the reflective path

DM0013 names each of them at build time, so nothing is silent today. Closing them is what lets
`Decorate(IServiceCollection, Type, Type)` be deleted, and the AOT probe already proved deletion is
the *last* step rather than a prerequisite: once an application emits no reflective call, ILC never
roots the overload and the publish is warning-clean with it still present.

#### Module-level `[Decorate(typeof(IFoo), typeof(FooDecorator))]` — done

`GetModuleDeclaredDecorators` reads the two arguments as rendered type names. There is no
declaration behind them, so there is no constructor to emit a `new` from.

**Resolved from the compilation at emission time.** `SymbolConstructorReader` reads the constructor
from an `INamedTypeSymbol`; `ModuleDecoratorResolver` finds the symbol with `GetTypeByMetadataName`
and fills in the model. Unbound generic arguments are legal in a `typeof` and arrive with their
arity, which the lookup restores as a backtick suffix.

It turned out the reader already half-existed: `MetadataCandidateUtility.GreediestConstructor` in the
conventions package did the same job for referenced-assembly scanning, but dropped parameter
attributes and did not honour `[ActivatorUtilitiesConstructor]`. It now delegates to the shared one,
so both paths gain what the other had.

**No public constructor is a diagnostic, not a fallback** — generated code constructs the decorator,
so there is nothing to emit, and saying so at build time is the whole point.

Three things make this the right shape rather than a workaround:

- **It is the same piece of code three other features need.** `ServiceModelUtility.GetConstructorInfo`
  is syntax-driven; a symbol-driven equivalent is exactly what
  `convention-registration-and-decorators.md` lists as *"the one genuinely new piece of code"* for
  scanning referenced assemblies, and what Part 8 needs to read a package's `[Decorator]`. Build it
  once.
- **It works across an assembly boundary for free**, which the syntax path never can — and
  `[Decorate]` exists precisely for services you do not own.
- **It costs nothing when unused.** The combine re-runs each keystroke by construction, but it does
  work only for modules that actually declare `[Decorate]`, and the result is wrapped so nothing
  downstream re-runs. Same pattern as `MetadataCandidateUtility`.

#### A generic decorator over convention-registered handlers — the merge, and more than file moving

The closings exist in this compilation. They are computed by the other analyzer assembly, which is
the whole of case B.

**But the merge is not "put the generators in one assembly".** `ConventionMatcher.Match` runs inside
`RegisterSourceOutput`, deliberately, because that is where it can report diagnostics — so its
`ServiceModel`s do not exist in any provider for `Expand` to combine with. Merging the assemblies
alone changes nothing.

What closes it is **one emission stage that sees attribute registrations, convention registrations
and decorators together**. That is what the original brief meant by "a single combined provider
feeding one emission stage", and it is the real content of step 7 — the file moving is the easy half.

Sequenced that way, the expansion machinery needs no change at all: `Expand` already takes a list of
registered service types and does not care which path produced them.

#### A generic decorator over handlers this compilation never sees

A package's handlers, or a hand-written `services.Add…` in `Program.cs`. These closings genuinely do
not exist at compile time, so no monomorphisation is possible and no amount of merging helps.

Part 8's closer-plus-manifest is the answer, and it is the only one. Until it exists this is a
**documented contract narrowing**, not a defect to fix quietly: a generic decorator covers what a
module registered in this compilation. DM0013 says so per decorator.

#### In the meantime

`<WarningsAsErrors>DM0013</WarningsAsErrors>` turns the whole thing into a build failure for a
project that publishes AOT. That needs no code — it is ordinary MSBuild — and it should be in the AOT
guide rather than invented as a bespoke property.

### Step 5 — make the candidate pipeline fast. Done, and the gate is answered.

`DeclarationStamp` + `ConventionCandidateCache` wrap the candidate transform, and `LocationModel`
now narrows to the declaration's identifier token.

Measured with `benchmarks/DependencyModules.Benchmarks`, per keystroke:

| classes | matching | before | after |
|---|---|---|---|
| 2,000 | 0 | 11.3 ms | **11.0 ms** |
| 2,000 | 500 | 20.9 ms | **10.9 ms** |
| 2,000 | 2,000 | 39.0 ms | **11.4 ms** |

**The shape changed, not just the number.** The cost used to scale with how many types a convention
matched; it is now flat. That was the property that made conventions frightening on a large project.

And what the package costs a project that references it, at 2,000 classes:

| | cold | per keystroke |
|---|---|---|
| `SourceGenerator` alone | 56.8 ms | 5.7 ms |
| both packages, before | 122.4 ms | 15.6–47.5 ms |
| both packages, after | 143.3 ms | **8.2 ms, flat** |
| **what conventions add** | +86.6 ms | **+2.4 ms** |

**On the gate: 2.4 ms, against a stated budget of 2.0 ms.** It misses, narrowly. Read as written the
merge waits; read as "is this affordable", a 39% increase on a generator that was already running is
a different proposition from the 3× it was before. That is a call to make deliberately rather than to
round in either direction.

The cold regression is real and is the stamp hashing every tree once: +18 ms on a 2,000-class build.
It buys 4.4× on every keystroke after it. If it needs reducing, the walk currently calls `ToString()`
on base lists and attribute lists, which allocates.

### Step 5 (original plan, for reference)

Measure the post-initialization question first. Then, in order of measured value:

1. **Split the transform.** Syntax-only model: name, namespace, arity, base-list simple names,
   attribute simple names, accessibility. No symbol binding.
2. **`LocationModel` keyed on the identifier token, without line/character.** `GetLineSpan` is over
   half the cost for a class with no base list, and the full-declaration span is what makes an edit to
   a method body invalidate the model. Rebuild line positions at output time from the compilation,
   which is already combined in, for the few candidates that actually report a diagnostic.
3. **Defer semantic binding to a stage that only sees survivors** — filtered by namespace, name and
   attribute syntactically, and by base-list simple name for assignability conventions. Take a
   `SyntaxReference` only for a declaration with a base list; nothing without one can satisfy an
   assignability convention.
4. **Cache the whole transform on `(node, declarationStamp)`** in a `ConditionalWeakTable`. This is
   the largest single win and it should be done first, not last: measured 4.1 ms → **2.0 ms** at 2,000
   classes on a method-body edit, with 1,999 of 2,000 nodes hitting.

   The stamp is what makes caching a *semantic* result sound. The node alone is not a valid key —
   proven by moving a `global using` in another file and watching an untouched node bind to a
   different `IRepo`. The stamp hashes everything that can change a binding (usings, extern aliases,
   namespace names, type identifiers, base lists, type parameters, modifiers, member signatures, the
   reference set) and excludes method bodies. Cached per `SyntaxTree`, it costs 0.03 ms per keystroke.

   **Two silent-failure risks to close in review.** Anything the stamp omits that affects binding
   yields a stale cache and a wrong registration with a green build — so it must be conservative, and
   there must be a test that a base-list change in another file invalidates. And a 32-bit hash is not
   an acceptable key when a collision means that; use a wider hash, or
   `SyntaxNode.IsEquivalentTo(other, topLevel: true)`, which is Roslyn's own ignore-bodies comparison
   and the primitive the stamp re-implements.

Not to be built: an intern table for `TypeDefinition` (measured 2.4× slower than allocating), an
MSBuild gate on the scan (measured to gate nothing), or fewer diagnostics (they cost 0.05 ms per
2,000; the eager location capture feeding them is the cost).

### Step 6 — move the contracts to `DependencyModules.Runtime`. Done.

`IConventionModule`, `IConventionDefinitions` and `IConventionRegistration` are now public types in
`DependencyModules.Runtime/Conventions/ConventionContracts.cs`, in the
**`DependencyModules.Runtime.Conventions`** namespace.

The first attempt kept the old `DependencyModules.Conventions` namespace for source compatibility.
That was wrong: it put the runtime contracts and the analyzer that reads them in one namespace across
two assemblies, which is ambiguous wherever both are referenced — the same class of problem the move
was meant to end. The namespace now matches the assembly, and consumers update one `using`.

Note the analyzer keeps its own `DependencyModules.Conventions` namespace, so
`ConventionContractSource.Namespace` deliberately names a namespace that is **not** the one the
analyzer lives in. `ConventionContractTests` is what stops the two drifting. `ConventionContractSource.Source` and the only
`RegisterPostInitializationOutput` in the repository are gone; the class survives as the three name
constants the analyzer matches on.

- **The wart it was meant to retire is retired**, and there is now a test proving it:
  `AnImplicitPublicImplementationDeclaresConventions` compiles
  `public void Conventions(IConventionDefinitions)`, which was CS0051 for as long as the contracts
  were emitted `internal`. `TheExplicitImplementationStillDeclaresConventions` pins that nobody has
  to rewrite anything.
- **The cost is visible and was measured by the build itself**: `PublicApiTests.RuntimeApi` failed,
  as it should, and the snapshot shows **45 lines added to Runtime's public API** — three interfaces
  and their verbs. That is the API users already write; it is now versioned.
- **The coupling risk is closed.** `ConventionContractTests` asserts the analyzer's string constants
  match the Runtime types, so renaming `IConventionModule` cannot silently stop every convention
  matching. It also asserts every verb returns `IConventionRegistration`, so an addition cannot
  quietly end the chain.

The stale comment in `FindConventionsMethod` explaining why explicit implementation was the only form
that compiled has been corrected rather than deleted — it records why the shape exists.

### Step 7 — fold the generator in, delete the package

`ConventionGenerator` joins `SourceGenerator.AttributeSourceGenerators()`.
`DependencyModules.Conventions` is reduced to a transitional empty package depending on
`DependencyModules.SourceGenerator`. The duplicated `Impl` compilation goes with it.

**Gate:** do not take this step until step 5 has the benchmark under 2 ms incremental at 2,000
classes with zero matches.

### Step 8 — cross-assembly generic decorators

The closer-plus-manifest design from Part 8 of the investigation. Larger than everything above and
independent of it; keep it last.

### Step 9 — correct the documentation

`website/guide/aot.md` currently lists decorators under *what this covers*. Until step 2 lands that
is false for every decorator, and after step 4 it is true only for registrations a module made. The
status line of `convention-registration-and-decorators.md` says open generic decorators are
implemented; it needs the reversal recorded.

---

## What breaks

| | Break | Mitigation |
|---|---|---|
| `DecoratorHelper.Decorate(IServiceCollection, Type, Type)` | removed | Binary break. Generated code is regenerated; hand-written callers move to the generic overload |
| `DependencyModules.Conventions` package | emptied | Transitional package plus a release note |
| `void IConventionModule.Conventions(…)` | explicit implementation no longer required | Source-compatible — explicit implementation still compiles |
| `services.Count` around a decorated service | grows by one | Test-only |
| A generic decorator over a hand-registered service | no longer decorated | Documented contract narrowing plus `VerifyDecoratorCoverage()` in the testing package |

All of it is affordable at `1.0.0-rc` and none of it is affordable after 1.0, which is the argument
for doing it now.

---

## Test plan

Behavioural, using `GeneratedAssembly` — compile, load, resolve. Do not assert on generated text.

- **Disposal**: a scoped `IDisposable` behind a decorator is disposed when the scope ends; the
  decorator is too; stacked decorators dispose in order. *(The first two are committed and failing.)*
- **Displacement**: the displaced registration keeps the original lifetime; a singleton inner is one
  instance across resolutions; a keyed registration keeps its key and gets a distinct private key.
- **Monomorphisation**: a generic decorator over two closed registrations wraps each with its own
  closed type; a value-type type argument resolves.
- **Dedup**: two implementations behind one closed service are each wrapped exactly once.
- **AOT**: an integration project published `PublishAot` that resolves a decorated generic service —
  the only test that would have caught any of this.
- **Incremental**: editing an unrelated method body leaves the convention emission cached, asserted
  on `TrackedSteps` rather than on wall clock.

`DependencyModules.Testing` gains `VerifyDecoratorCoverage()`, which may use reflection — it never
ships in a published application, and it is the only place the silent gap from step 4 is catchable.
