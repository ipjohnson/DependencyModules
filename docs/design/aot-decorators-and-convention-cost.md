# Design: AOT-safe decorators, and what conventions actually cost

Status: investigation. Nothing here is implemented. Every number and every failure below was produced
by running something, and the harnesses are described so they can be re-run rather than trusted.

This document **reverses two claims** made elsewhere in the repository:

- `docs/design/convention-registration-and-decorators.md` lists open generic decorators under
  *Done*, and `website/guide/aot.md` lists decorators under *What this covers*. Neither holds.
  **No decorator of any kind works under Native AOT today** — not generic ones, and not the
  non-generic case the design doc treats as settled.
- The reason to merge `DependencyModules.Conventions` into `DependencyModules.SourceGenerator` was
  taken to be a hard blocker for AOT-safe generic decorators. It is a blocker for **one of the two**
  registration paths, and the cheaper of the two fixes needs no merge at all.

- [Part 1: the AOT defect](#part-1-the-aot-defect)
- [Part 2: the two fixes, both verified](#part-2-the-two-fixes-both-verified)
- [Part 3: the constraint monomorphisation imposes](#part-3-the-constraint-monomorphisation-imposes)
- [Part 4: what conventions cost, measured](#part-4-what-conventions-cost-measured)
- [Part 5: what is worth caching, and what is not](#part-5-what-is-worth-caching-and-what-is-not)
- [Part 6: the merge question, answered](#part-6-the-merge-question-answered)
- [Part 7: third-party frameworks on top of this](#part-7-third-party-frameworks-on-top-of-this)
- [Part 8: open generic decorators across an assembly boundary](#part-8-open-generic-decorators-across-an-assembly-boundary)
- [Part 9: a runtime with no reflection](#part-9-a-runtime-with-no-reflection)
- [Part 10: sequencing](#part-10-sequencing)

---

## Part 1: the AOT defect

### How it was established

A console application was published `PublishAot` for `osx-arm64`, net8.0, ILCompiler 8.0.29, and
**run**. It declares two handlers behind `IRequestHandler<TRequest, TResponse>` — one with a
reference-type response, one with `int` — an open generic `[Decorator]` over them, a non-generic
`[Decorator]` over an unrelated interface, and a plain `[SingletonService]` as a control.

Native AOT was chosen over reading warnings because IL3050 is a warning, and the standing
counter-argument to a warning is "but does it actually break." It does.

### What the published binary prints

```
reference-type response (IRequestHandler<CreateOrder, OrderId>):
  FAILED: NotSupportedException: 'LoggingHandler`2[CreateOrder,OrderId]' is missing native code or metadata.

value-type response (IRequestHandler<CountRequest, int>):
  FAILED: NotSupportedException: 'LoggingHandler`2[CountRequest,System.Int32]' is missing native code or metadata.

control: plain attribute registration, no decorator involved:
  resolved Log

control: NON-generic decorator (IGreeter):
  FAILED: InvalidOperationException: A suitable constructor for type 'ShoutingGreeter' could not be located.
```

Three things in that output are worth not glossing over.

**The reference-type case fails too.** The expectation going in was that reference-type arguments
would survive on shared canonical code and only a value-type argument would break. They both break,
and for a reason that makes the value-type distinction irrelevant: ILC never compiles *any*
instantiation of `LoggingHandler<,>`, because nothing in the emitted code constructs one. The
generated call passes `typeof(LoggingHandler<,>)` — the open definition — and that is not a
statically reachable instantiation. There is no canonical body to share.

**The non-generic decorator fails as well**, and this is the finding that was not anticipated at all.
It has nothing to do with generics. `DecoratorHelper.Decorate(IServiceCollection, Type, Type)` passes
`decoratorType` to `ActivatorUtilities.CreateInstance`, whose parameter carries
`[DynamicallyAccessedMembers(PublicConstructors)]`. The helper's own parameter carries no such
annotation, so the requirement stops there and the trimmer has no reason to keep the constructor.
`typeof(ShoutingGreeter)` roots the *type*; it does not root its constructors.

**Plain registration is fine.** Whatever is wrong is specific to the decoration path.

### What the toolchain says on its own

`dotnet build src/DependencyModules.Runtime -p:IsAotCompatible=true -p:TargetFramework=net10.0`
produces five warnings, four of them in `DecoratorHelper`:

| Location | ID | Call |
|---|---|---|
| `DecoratorHelper.cs:91` | IL3050, IL2055 | `Type.MakeGenericType` — `RequiresDynamicCode` |
| `DecoratorHelper.cs:94` | IL2067 | `ActivatorUtilities.CreateInstance`, unannotated `decoratorType` |
| `DecoratorHelper.cs:137` | IL2075 | `GetInterfaces()` on an unannotated type |
| `DependencyRegistry.cs:102` | IL2067 | `ServiceDescriptor(Type, object, Type, ServiceLifetime)`, unannotated `implementationType` |

`DependencyModules.Runtime.csproj` sets no `IsAotCompatible`, which is why none of this has ever
appeared in a build. **Turning it on is the single change that would have caught this**, and it
should be turned on regardless of what else here gets built.

The `DependencyRegistry.cs:102` warning is on the main registration path rather than the decorator
one. The control above resolved successfully, so it is not currently breaking anything observable —
but it is the same missing-annotation shape, on a public API, and it should be annotated rather than
left to luck.

---

## Part 2: the two fixes, both verified

Both were written by hand exactly as the generator would emit them, published Native AOT, and run.

### Fix 1 — annotate the parameter

```csharp
public static void Decorate(
    IServiceCollection services,
    Type serviceType,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type decoratorType)
```

Result in the published binary:

```
PROPOSED FIX 1: non-generic decorator via an ANNOTATED Type parameter:
  resolved as AnnotatedShouter
  result QUIET!
```

Nothing else changed — still `ActivatorUtilities`, still a `Type`. **This is a one-line change that
makes every non-generic decorator work under Native AOT**, and it is independent of everything else
in this document. It does not help the generic case: `MakeGenericType` is `RequiresDynamicCode` and
no annotation reaches that.

### Fix 2 — monomorphise the generic case

Emit one closed call per closed registration instead of one open-generic call:

```csharp
// today, once per decorator
DecoratorHelper.Decorate(services, typeof(IRequestHandler<,>), typeof(LoggingHandler<,>));

// proposed, once per (decorator, closed registration) pair
DecoratorHelper.Decorate(services, typeof(IRequestHandler<CreateOrder, OrderId>),
    (p, inner) => new LoggingHandler<CreateOrder, OrderId>((IRequestHandler<CreateOrder, OrderId>)inner));

DecoratorHelper.Decorate(services, typeof(IRequestHandler<CountRequest, int>),
    (p, inner) => new LoggingHandler<CountRequest, int>((IRequestHandler<CountRequest, int>)inner));
```

Result:

```
reference-type response (IRequestHandler<CreateOrder, OrderId>):
  resolved as LoggingHandler`2
  [decorator] CreateOrder -> OrderId
  result OrderId { Value = abc }

value-type response (IRequestHandler<CountRequest, int>):
  resolved as LoggingHandler`2
  [decorator] CountRequest -> Int32
  result 42
```

Both work, including the value-type instantiation. `MakeGenericType`, `ActivatorUtilities` and the
`GetInterfaces()` walk in `ResolveTypeArguments` all disappear from the path.

**No new runtime API is needed.** `DecoratorHelper` already exposes the
`Func<IServiceProvider, object, object>` overload this uses, and `ConstructorInfoModel` — which the
generator needs in order to fill the decorator's remaining constructor parameters — is already
computed for every service model.

One measurement artefact worth recording, because it cost a publish cycle to find: the first attempt
at fix 2 left `[Decorator]` on the class, so the shipped open-generic call still ran and wrapped the
registration *first*. The closed decoration then wrapped the broken wrapper. **A monomorphised
emission and the open-generic emission cannot both be applied to the same service.**

---

## Part 3: the constraint monomorphisation imposes

This is the part that decides the shape of the feature, and it is not in the existing design doc.

There are two ways a closed registration of a generic service comes into being, and they live in
different places:

| | Registration produced by | Decorator discovered by | Same analyzer assembly? |
|---|---|---|---|
| A | `[SingletonService] class CreateOrderHandler : IRequestHandler<CreateOrder, OrderId>` | `ForAttributeWithMetadataName` over `[Decorator]` | **yes** — both in `DependencyModules.SourceGenerator` |
| B | a convention matching `IRequestHandler<,>` | same | **no** — registration is in `DependencyModules.Conventions` |

**Case A needs no merge.** `ServiceSourceGenerator` and `DecoratorSourceGenerator` are both returned
from `SourceGenerator.AttributeSourceGenerators()` and both receive the same
`IncrementalValuesProvider` in one `Initialize`. Their providers can be combined into a single
emission stage without any cross-generator ordering, because there is no cross-generator anything —
it is one generator with two `RegisterSourceOutput` calls today, and one with three would be an
ordinary refactor. Case A is the MediatR-in-one-assembly shape and covers most real usage.

**Case B needs the two halves to meet.** That is the merge the brief was about, and Part 6 prices it.

### What monomorphisation cannot do, at all

A generic decorator shipped in a *package*, wrapping handlers registered by the *consuming
application*, cannot be monomorphised. Package `P` is compiled before the application exists; the
closed constructions do not exist yet; there is no `new Logging<A, B>(...)` for the compiler to
write.

That scenario works today, and it works **precisely because** the emitted call is an open-generic
runtime operation: `P` emits `ApplyDecorator0`, `ApplyDecorators` runs it after every module's
services are registered, and it rewrites descriptors it never saw at compile time. The design doc
records this as the foundation for a future mediator package.

So the two properties are mutually exclusive:

| | cross-assembly generic decoration | Native AOT |
|---|---|---|
| open-generic runtime call (today) | works | **broken** |
| monomorphised emission, application writes `new` | impossible | works |

**Superseded by Part 8**, which is worth reading before acting on this table. A third shape — the
package emitting a generic closer that the application calls with closed type arguments — is both,
and has been published Native AOT across a real assembly boundary and run. The row above is only
true of the application constructing the decorator itself.

There is a way to have both, and it is the one the codebase is already equipped for: **the consuming
application's generator discovers `[Decorator]` in referenced assemblies from metadata** and emits
the closed decorations itself. `MetadataCandidateUtility` already walks
`IAssemblySymbol.GlobalNamespace` for convention candidates; reading attributes off those symbols is
the same walk. That converts case B-across-assemblies into case B-in-one-compilation.

Until that exists, the honest position is that monomorphisation covers what one compilation can see,
and anything else keeps the runtime path and is **not** AOT-safe. That is a diagnostic, not a silent
degradation.

### The double-decoration hazard

If both analyzer assemblies emit closed decorations for their own registrations, they are not
disjoint. `Decorate` loops over *every* descriptor matching the service type, so two calls naming
`IHandler<A,B>` — one from the attribute path, one from the convention path — wrap both
implementations twice.

The registrations themselves are disjoint (a class carrying a service attribute is never a
convention candidate, enforced by `ConventionCandidateUtility.ServiceAttributeNames`), but the
*service types* are not. Two implementations of one closed service, one attributed and one by
convention, is an ordinary thing to write.

Either emit from one place, or make `Decorate` refuse to apply the same decorator type to a
descriptor twice. The second is a small amount of per-collection bookkeeping and is worth having
anyway, since it also makes the phase idempotent.

---

## Part 4: what conventions cost, measured

Harness: `CSharpGeneratorDriver` over 2,000 synthetic classes, one class per syntax tree, real method
bodies, `DOTNET_TieredCompilation=0`, median of 11. "incr" is the second run of one driver after
replacing a single syntax tree — one method body edited. This is the same shape as
`benchmarks/DependencyModules.Benchmarks`, extended to count provider invocations and to read
`GeneratorRunResult.TrackedSteps`.

### The dominant cost is not the scan. It is the transform, and it re-runs every time.

Recording every transform invocation with the tree it came from, and whether the node is the same
object as last run:

```
RegisterPostInitializationOutput = True
  run 1 (cold)            pred=242290  transformCalls=2001  distinctNodes=2001  distinctTrees=2001
  run 2 (one tree edited) pred=121     transformCalls=2001  distinctNodes=2001  distinctTrees=2001
                          of those, from the edited tree: 1
                          node instances also seen last run: 2000/2001
  run 3 (nothing changed) pred=0       transformCalls=2001

RegisterPostInitializationOutput = False
  run 2 (one tree edited) pred=121     transformCalls=2001  (from the edited tree: 1)
  run 3 (nothing changed) pred=0       transformCalls=0
```

Read those four numbers together, because each rules something out:

- **`pred=121`** — the predicate ran only over the edited tree. Per-tree filtering is cached and
  working. The scan is not the problem.
- **`transformCalls=2001`, `from the edited tree: 1`** — the transform ran for 2,000 candidates whose
  trees did not change. Roslyn keeps the *filtered node list* per tree and re-executes the transform
  over all of it.
- **`node instances also seen last run: 2000/2001`** — those are the same `SyntaxNode` objects,
  by reference. Nothing about them changed; the work is simply repeated.
- **run 3** — with no post-initialization output a no-change run short-circuits entirely; with one it
  does not. **That is the only thing post-init affects.** An earlier revision of this document blamed
  it for the general case, which the middle two rows refute: a real edit re-runs everything either
  way.

So incrementality here comes entirely from the *model* comparing equal and stopping propagation. It
does not come from the transform being skipped, and no amount of model tuning changes that.

### Where the wall clock goes, at 2,000 classes, on one keystroke

The same pipeline with only the transform body swapped:

| | |
|---|---|
| transform does the real work (as shipped) | **13.9 ms** |
| transform is syntax only | 2.4 ms |
| transform returns a constant | 1.2 ms |
| **attributable to the transform body** | **12.7 ms** |
| driver overhead with nothing to do | 1.2 ms |

**91% of a keystroke is the transform re-deriving 2,000 models that are byte-identical to the ones it
derived on the previous keystroke.** One of the 2,001 is genuinely new.

That fixes where the effort goes, and it makes the node-keyed cache the obvious first move rather
than a micro-optimisation: the nodes are reference-identical 2,000 times out of 2,001, and a
`ConditionalWeakTable` lookup costs 0.04 ms per 2,001 against 1.15 ms to recompute the syntax half
alone.

### The semantic half can be cached too — on the node *and* a declaration stamp

An earlier revision of this document said the semantic half could not be cached. That was wrong, and
the correction matters because it roughly halves the remaining cost.

What is true is narrower: it cannot be keyed on the **node alone**. That failure is real and
reachable, not theoretical — `Handler.cs` is never touched, and a `global using` moved in a different
file changes what it binds to:

```
edit: global using moved from N1 to N2
  edited file                         : /bench/File0.cs
  Handler.cs tree is the same object  : True
  Handler's node is the same object   : True
  Handler's syntax text is identical  : True
  resolved interface before           : N1.IRepo
  resolved interface after            : N2.IRepo
  >> semantic answer changed          : True
```

A node-keyed cache would serve `N1.IRepo` and register the wrong service with a green build.

The fix is to key on `(node, declarationStamp)`, where the stamp hashes everything in the compilation
that can change what a name binds to — usings, extern aliases, namespace names, type identifiers,
base lists, type parameter lists, modifiers, member signatures, and the reference set — and
deliberately **excludes method bodies**, since nothing inside one can change another file's binding.
Cached per `SyntaxTree`, which is immutable and reference-stable, so only the edited tree recomputes:

| | |
|---|---|
| stamp, cold — every tree hashed | 28.55 ms |
| stamp, nothing changed | **0.03 ms** |
| stamp, after a method body edit | **0.04 ms** |
| unchanged by a method body edit | yes |
| changed by a base-list edit | yes |

End to end, same pipeline, median of 11, only the cache differing:

| 2,000 classes, one method body edited | |
|---|---|
| **with the cache** | **2.0 ms** (hits 1,999, misses 1) |
| without | 4.1 ms |

and the pieces, per 2,000 nodes: `DeclarationStamp.Of` memoised on the compilation **0.02 ms**,
fetching the semantic model 0.10 ms, `GetCandidateModel` — what a hit avoids — **2.36 ms**.

**Two things to get right, because both fail silently.** The stamp must be conservative: anything it
omits that can affect binding produces a stale cache and a wrong registration with a green build. And
a 32-bit hash is not enough for a key whose collision means exactly that — use a wider hash, or
compare with `SyntaxNode.IsEquivalentTo(other, topLevel: true)`, which is Roslyn's own
ignore-method-bodies comparison and is the primitive this stamp is re-implementing.

The syntactic pre-filter is still worth having — it makes a *cold* build cheap and bounds the misses
after a declaration edit, which the cache cannot help with. But it is now an optimisation on top of
the cache rather than the only route.

### Where the transform's time goes, per 2,000 declarations

| Step | no base list | implements one interface |
|---|---|---|
| `GetTypeDefinition()` | 0.43 ms | 0.40 ms |
| **`LocationModel.From(node)`** | **1.15 ms** | 0.60 ms |
| — of which `GetLineSpan()` | 0.58 ms | 0.27 ms |
| `GetConstructorInfo` | 0.81 ms | 0.55 ms |
| `EnvironmentConditionUtility.GetConditions` | 0.07 ms | 0.05 ms |
| `GetDeclaredSymbol` + `AllInterfaces` walk | — | **4.16 ms** |
| **full `GetCandidateModel`** | **2.06 ms** | **7.16 ms** |

For the majority population — a class with no base list, which is most classes in most projects —
**`LocationModel.From` is over half the cost**, and it is paid on every keystroke for every class.
It exists to place DM0006 and DM0010, which fire for a handful of types.

For comparison, 2,000 pairwise `ConventionCandidateModel.Equals` calls take **0.08 ms**. Model
equality is not where the time is.

### Provider floors, 2,000 classes

| Shape | cold | incr |
|---|---|---|
| predicate over every node, empty transform | 32.9 ms | **0.7 ms** |
| `ForAttributeWithMetadataName` over `[Decorator]` | 3.5 ms | **0.1 ms** |
| shipped `ConventionSourceGenerator` | 113.7 ms | 23.5 ms |
| shipped `SourceGenerator` (services + decorators) | 55.4 ms | 5.6 ms |

Two things follow. **Visiting every syntax node is a cold-build cost, not a per-keystroke cost** —
0.7 ms once the predicate results are cached. And **FAWMN is free enough to ignore**: whatever a
merged generator needs to know about `[Decorator]`, it can have for 0.1 ms per keystroke.

### Transform variants, 2,000 classes, incremental

| Transform | 0 matching | 500 matching | 2000 matching |
|---|---|---|---|
| as shipped (binds symbols for every candidate) | 11.3 ms | 20.9 ms | 39.0 ms |
| syntax-only | **1.7 ms** | **1.8 ms** | **1.7 ms** |
| syntax-only, semantics deferred to a later stage | 9.6 ms | 10.8 ms | 9.6 ms |

The syntax-only row is the floor, and it is flat — it does not care how many types match, because it
never binds anything.

The deferred row is the design the brief proposed: syntax-only transform, then a stage combined with
`CompilationProvider` that re-binds only survivors of a syntactic pre-filter. It beats the shipped
shape in every column, by 4× at 2,000 matches — but it costs **more** on a cold build (74–115 ms
versus 53–91 ms), because the collected array has to be walked on every compilation change and a
`SyntaxReference` has to be taken per candidate that could match. It is a real improvement and not a
dramatic one; the flat 1.7 ms row is what shows how much of the remaining cost is inherent.

### What actually invalidates emission

Reading step reasons after editing one method body:

| Pipeline | candidates | collected | emission |
|---|---|---|---|
| as shipped | Cached=2000 Modified=1 | Modified | **Unchanged** (ran, same text) |
| location out of model equality | Cached=2001 | Cached | **Cached** (did not run) |
| editing a file holding no candidate | Cached | Cached | Cached |

`LocationModel` carries the declaration's **full span**, so editing anything inside a class body
changes `SpanLength` and the model no longer compares equal. Emission then re-runs
`ConventionMatcher` over every candidate and re-renders the file — and throws the result away,
because the text is identical.

End to end this is worth about **1 ms of 16–46 ms**, so it is not the headline. It is still free to
fix, and the fix is to key the location on the **identifier token** rather than the declaration:
editing a method body does not move the class name, so the common IDE edit stops invalidating
anything.

**The combine chain is not the invalidation source.** `metadataCandidates` re-runs on every keystroke
by construction — it combines with `CompilationProvider` — and reports `Unchanged`, because
`EquatableList` compares by value. That wrapper is doing its job, and research question (3) is
answered: no restructuring needed there.

### An MSBuild property cannot gate the scan

| | cold | counters |
|---|---|---|
| `DependencyModules_EnableConventions=false` | 51.6 ms | pred=242290 **xform=2001** |
| `DependencyModules_EnableConventions=true` | 51.1 ms | pred=242290 **xform=2001** |

Identical. `AnalyzerConfigOptionsProvider` is a provider, so its value is only available *after* the
syntax provider has produced values — a gate applied there discards results whose cost has already
been paid. There is no overload of `CreateSyntaxProvider` that takes a condition, and `Initialize`
cannot read build properties synchronously.

**Research question (2) is answered: no.** Not loading the analyzer at all — the package boundary —
is the only thing that gates this.

Related, and unfixable either way: `RegisterPostInitializationOutput` emits the 413-line
`IConventionDefinitions` contract into every compilation that loads the generator, unconditionally.
A merge means every project that uses this library compiles that file.

---

## Part 5: what is worth caching, and what is not

Three plausible-looking optimisations were measured. One is a large win, one is a small win, and one
is a **loss** — which is the reason to measure rather than reason.

### Dropping diagnostics buys nothing. Do not do it.

| | |
|---|---|
| 2,000 DM0010 diagnostics — `Location.Create` + `Diagnostic.Create` | **0.05 ms** |
| `Location.Create` alone, 2,000 times | 0.02 ms |
| `LocationModel.From` in the transform, 2,000 candidates | **1.15 ms** |

The diagnostics are two per cent of what the machinery feeding them costs, and they are only paid
when emission actually runs. **The expense is capturing a location eagerly for every candidate on
every keystroke, so that a handful of them can be reported.**

DM0010 is also the single thing the design doc names as what Scrutor structurally cannot do —
"this type is in the container as `IFoo`, via `IAuditedFoo`", answered in the IDE, at the type.
Deleting it to save 0.05 ms would be trading the differentiator for nothing.

What to change instead:

- **Drop the line/character half of `LocationModel`.** `GetLineSpan()` is 0.29–0.58 µs per node and
  needs the text line index; `FilePath` + `Span` is 0.15 µs. Rebuild the line positions at output
  time, where the compilation is already in hand via the existing `Combine`, and only for the few
  candidates that actually produce a diagnostic.
- **Key the span on the identifier token**, not the declaration. Editing a method body does not move
  the class name, so the common IDE edit stops invalidating the model — the Part 4 finding, fixed by
  the same change.
- If DM0010 ever does become load-bearing on cost, it can be gated on an MSBuild property. **That
  gate works**, unlike the one in Part 4, because diagnostics are produced at output time where
  `AnalyzerConfigOptionsProvider` has already delivered its value.

### Interning `TypeDefinition` is a loss

`TypeDefinition.Get` allocates a fresh instance per call, and `GetHashCode()` is
`ToString().GetHashCode()`, so the first hash of every instance allocates a string. Both look like
obvious candidates for a cache. Measured:

| | |
|---|---|
| 4,000 × `Get` + `HashSet.Add`, fresh instances | **0.18 ms** |
| 4,000 × `Get` + `HashSet.Add`, interned through a dictionary | **0.44 ms** |
| 200,000 × `Equals` on distinct instances | 1.69 ms |
| 200,000 × `Equals` with a reference fast path | **0.06 ms** |

**Interning is 2.4× slower than allocating.** A gen-0 allocation is a pointer bump; a tuple-keyed
dictionary lookup that hashes two strings is not. The allocation was never the problem.

Equality is 28× faster when instances are shared, but that only matters in a hot loop, and there
isn't one: 2,000 pairwise `ConventionCandidateModel.Equals` calls take 0.08 ms. Adding a
`ReferenceEquals` fast path to `Equals` is free and harmless; building an intern table to feed it is
not worth it.

### Caching the syntax-derived half on the node is a 29× win

| 2,001 candidates | |
|---|---|
| compute `GetTypeDefinition()` + `LocationModel.From` | 1.15 ms |
| `GetTypeDefinition()` + identifier span only, no `GetLineSpan` | 0.69 ms |
| `ConditionalWeakTable<SyntaxNode, …>` lookup | **0.04 ms** |

Because the transform re-runs for every candidate on every driver run (Part 4), a cache keyed on the
syntax node gives back the incrementality Roslyn is not providing.

**The soundness line matters and is not negotiable.** Node identity implies *syntax* identity, so
anything derived purely from syntax — the type definition, the location, the declared modifiers — is
safe to cache this way. Anything bound through the semantic model is **not**: an edit in another file
can change what a base-list name resolves to, and serving a stale interface list would register the
wrong service with a green build. Cache the syntax half; recompute the semantic half.

Two properties make this safe to add:

- `ConditionalWeakTable` holds keys weakly, so entries die with the node and nothing pins a syntax
  tree in memory — the failure the `LocationModel` comment already warns about.
- It is a cache, not a contract. Roslyn creates red nodes on demand and may collect and recreate
  them, so the hit rate is high but not guaranteed. A miss recomputes and is merely slower.

Rendered C# output does not need a cache of its own: once the model stops changing on unrelated
edits, emission is skipped entirely rather than re-rendered and discarded.

---

## Part 6: the merge question, answered

### What the boundary is worth today

2,000 classes, per keystroke:

| Project references | cold | incr |
|---|---|---|
| `SourceGenerator` only | 54.6 ms | **5.3 ms** |
| both packages, convention matches nothing | 122.4 ms | 16.7 ms |
| both packages, **module declares no conventions at all** | 122.4 ms | 15.6 ms |
| both packages, convention matches all 2,000 | 175.6 ms | 47.5 ms |

The third row is the one that decides it. A project that references the conventions package and
never writes a convention pays the same as one that writes a convention matching nothing — because
the scan runs either way. **The package boundary is currently worth about 10 ms per keystroke to a
non-user of conventions, at 2,000 classes.** It is a real thing and it is not enormous.

### The merge is not what is expensive

Merging changes *who* pays, not *how much* is paid. The work is identical; the difference is that
projects not using conventions start paying it. So the merge is affordable exactly to the extent
that the scan is cheap — and Part 4 shows the scan is dominated by a transform that can be made
roughly six times cheaper without changing what it computes.

Ordered by what they buy:

1. **Make the transform syntax-only where it can be** — `LocationModel` off the hot path, constructor
   info deferred. 11.3 ms → 1.7 ms per keystroke at 2,000 classes, 0 matching. This is worth doing
   whether or not anything merges, and it is what makes the merge cheap enough to argue about.
2. **Monomorphise case A** — attribute-registered services, entirely inside
   `DependencyModules.SourceGenerator`. No merge, no new provider, no cross-assembly anything.
3. **Then** decide about case B, with a scan that costs a fifth of what it costs today.

Doing (3) first means arguing about a 10 ms regression that (1) mostly removes.

### If case B is wanted without a merge

Both packages emitting their own closed decorations is viable — the `[Decorator]` input costs 0.1 ms
per keystroke via FAWMN, so duplicating that provider in the conventions package is free. It requires
the double-decoration guard from Part 3, and it means the decorator attribute is read twice. That is
a smaller change than merging two analyzer assemblies, and it keeps the "don't pay for what you don't
use" property that the boundary exists for.

Worth noting that the two assemblies already compile the same `Impl` sources, so every shared type is
declared twice today. A merge would remove that duplication, which is a genuine if minor argument in
its favour.

---

## Part 7: third-party frameworks on top of this

### The extension seam already exists, and it is already shipped

`DependencyModules.SourceGenerator.Impl` is a **source-only NuGet package**. Its own description says
so, and `Package/DependencyModules.SourceGenerator.Impl.targets` implements it:

```xml
<ItemGroup Condition="'$(PackageDependencyModuleIncludeSource)' == 'true'">
    <Compile Include="$(MSBuildThisFileDirectory)../src/**/*.cs" Visible="false"/>
</ItemGroup>
```

A framework sets that property, compiles the internals into its own analyzer assembly, subclasses
`BaseSourceGenerator`, and declares its own `[Generator]`. `DependencyModules.Conventions` is the
reference consumer and does exactly this via a project reference instead of the package.

`BaseSourceGenerator.ModuleAttributeTypes()` is the hook for a framework's own module attribute —
`[HardenedApplication]` rather than `[DependencyModule]`. It is `virtual`, documented for that
purpose, and **currently overridden by nothing in this repository**. It is a seam that has been built
and never exercised, which means it is unproven rather than proven.

### Hardened already uses this exact pattern — for its own generator

Read from `~/Hardened.Framework`: `Hardened.SourceGenerator.csproj` packs `**/*.cs` under `src/`,
sets `PackageCSharpAuthorIncludeSource=true`, and four leaf generators
(`Hardened.Library`, `Hardened.Console`, `Hardened.Web`, `Hardened.Templates`) each compile it in and
declare one `[Generator]`.

So the mechanism needs no selling: it is the same shape Hardened chose independently, down to the
vendored CSharpAuthor. What Hardened does **not** do today is use DependencyModules at all — it has
its own `DependencyInjectionIncrementalGenerator`, its own `KnownTypes.DI.Registry`, its own
`EntryPointSelector`. The question is not whether the seam is usable but whether the two DI
generators should become one.

### Stacking analyzer assemblies is cheap. The convention scan is not.

2,000 classes, per keystroke:

| Analyzer assemblies loaded | cold | incr |
|---|---|---|
| `SourceGenerator` only | 55.5 ms | 5.6 ms |
| + 1 extension generator | 54.0 ms | 5.5 ms |
| + 2 extension generators | 60.1 ms | 6.1 ms |
| + 3 extension generators | 60.2 ms | 5.9 ms |
| `SourceGenerator` + `Conventions` + 2 framework generators | **138.4 ms** | **19.6 ms** |

Each extra generator brings its own module-discovery `CreateSyntaxProvider` over every syntax node
and cannot share Roslyn's caches with the others — and it costs roughly **2 ms cold and 0.15 ms per
keystroke**. Module discovery is cheap enough that a framework stacking three or four generators on
this seam is a non-issue.

The last row is the whole point: adding three generators costs 5 ms, and adding the convention scan
costs 78 ms. **The scaling problem is not the number of extensions. It is the one provider that
transforms every class in the compilation** — the same finding as Part 4, arrived at from the other
direction.

### What this implies for the seam

- **The source-only package is the right answer and needs no redesign.** It is proven by
  `DependencyModules.Conventions`, it is the pattern Hardened already uses, and it costs almost
  nothing to stack.
- **Fixing the candidate transform is what makes the seam safe to recommend.** A framework that
  compiles in Impl inherits whatever the transform costs; today that is 11–39 ms per keystroke on a
  2,000-class project the moment conventions are involved.
- **`ModuleAttributeTypes()` should get a test before it is advertised.** Nothing exercises it, and
  an extension point that has never been used is a bug that has not been found yet.
- Worth deciding explicitly: a framework subclassing `BaseSourceGenerator` gets module discovery and
  emission, but the *service* attribute providers live in `DependencyModules.SourceGenerator`, not in
  Impl — except `ServiceSourceGenerator.cs`, which Impl packs specially. That asymmetry will be the
  first thing an integrator trips over.

---

## Part 8: open generic decorators across an assembly boundary

Part 3 stated that monomorphisation and cross-assembly generic decoration are mutually exclusive.
That is true of the *obvious* monomorphisation — the application emitting `new P.Behavior<X, Y>(…)`
itself. It is **not** true of the feature. There is a shape that gives both, and it has been built
and run.

### The problem, stated precisely

Package `P` ships:

```csharp
[Decorator]
public class LoggingBehavior<TRequest, TResponse>(
    IRequestHandler<TRequest, TResponse> inner, IAuditSink sink)
    : IRequestHandler<TRequest, TResponse>;
```

Application `A` declares `CreateOrderHandler : IRequestHandler<CreateOrder, OrderId>`.

- When `P` compiles, `CreateOrder` and `OrderId` do not exist. `P` cannot write the closure.
- When `A` compiles, everything exists — but `[Decorator]` sits on a type in a referenced assembly,
  and `ForAttributeWithMetadataName` does not see those.

**The closure can only be emitted in `A`.** There is no alternative: `new LoggingBehavior<CreateOrder,
OrderId>(…)` is a literal that only `A`'s compilation can produce. So the whole question is what `A`
has to learn from `P`, and who owns the knowledge of how to build `P`'s decorator.

### The answer: the package ships a generic closer, the application supplies the type arguments

`P`'s generator emits, next to the decorator, a generic static method:

```csharp
public static class LoggingBehaviorRegistration {
    public static void ApplyTo<TRequest, TResponse>(IServiceCollection services) =>
        DecoratorHelper.Decorate(services, typeof(IRequestHandler<TRequest, TResponse>),
            (provider, inner) => new LoggingBehavior<TRequest, TResponse>(
                (IRequestHandler<TRequest, TResponse>)inner,
                provider.GetRequiredService<IAuditSink>()));
}
```

`A`'s generator emits one **closed generic method call** per closed registration it made:

```csharp
LoggingBehaviorRegistration.ApplyTo<CreateOrder, OrderId>(services);
LoggingBehaviorRegistration.ApplyTo<CountRequest, int>(services);
```

A closed generic call is an ordinary static reference. ILC follows it, compiles the instantiation,
and through it `new LoggingBehavior<CreateOrder, OrderId>(…)`. No `MakeGenericType`, no
`ActivatorUtilities`, no interface walk, nothing to annotate.

### Verified

Two projects, a real assembly boundary, published Native AOT and run. `aotlib` holds the interface,
the decorator and the closer; `aotapp` holds the handlers and the calls, and never names
`LoggingBehavior<,>` anywhere.

```
A. package decorator applied through a generic closer in the package:
  resolved as LoggingBehavior`2
  [audit] CreateOrder -> OrderId
  result OrderId { Value = abc }
  resolved as LoggingBehavior`2
  [audit] CountRequest -> Int32
  result 42

B. same decorator applied the way the package emits it today:
  FAILED: InvalidOperationException: A suitable constructor for type
          'AotLib.LoggingBehavior`2[AotApp.CreateOrder,AotApp.OrderId]' could not be located.
```

The library itself builds `IsAotCompatible=true` at **zero IL warnings**. Every warning in the app's
publish comes from `DecoratorHelper`'s existing open-generic path, which case B still exercises.

**One detail in case B's failure is load-bearing.** It failed on the *constructor*, not on
"missing native code or metadata" as it did in Part 1. That is because case A's closed call had
already forced ILC to generate the `LoggingBehavior<CreateOrder, OrderId>` instantiation, so
`MakeGenericType` found it. The two failures are independent: rooting the instantiation fixes one,
the annotation from Part 2 fixes the other.

That is not a curiosity about the old path. It is the mechanism behind a defect the fix would
*introduce*, and it is severe enough to have its own section below.

### Emitting closed calls makes the runtime fallback fail selectively

Keeping the open-generic call as a compatibility fallback looks safe: anything the generator covers
is decorated statically, anything it misses falls back to the path that works today. **It is not
safe, and the reason is that the fallback stops failing uniformly.**

One application, one decorator, three registrations. The first goes through a module, so a closed
call is emitted for it. The other two are written by hand in `Program.cs` — ordinary code the
generator never sees — and only the fallback can reach them. Published Native AOT and run:

```
1. generator SAW this one (closed call emitted):
   resolved as LoggingBehavior`2, result OrderId { Value = abc }

2. generator NEVER saw it, both type arguments are reference types:
   resolved as LoggingBehavior`2, result RenameId { Value = xyz }        <- works

3. generator NEVER saw it, response is a value type:
   FAILED: NotSupportedException: 'LoggingBehavior`2[CountRequest,System.Int32]'
           is missing native code or metadata.
```

The same three under JIT: all pass.

Row 2 works **by accident**. The closed call in row 1 caused ILC to compile the canonical
`LoggingBehavior<__Canon, __Canon>` form, which every all-reference-type instantiation shares, so
`MakeGenericType(Rename, RenameId)` finds code that exists for an unrelated reason. Row 3 needs an
exact instantiation, which cannot be produced at run time and was never generated.

This is the failure profile the library exists to prevent:

- **It passes in development.** JIT resolves all three.
- **It passes for most types.** Reference-type arguments dominate real handler signatures.
- **It fails only under AOT, only for value-type arguments, only at resolve time**, in production.
- **Whether it fails depends on unrelated code.** Row 2 works because row 1 exists. Delete the
  module-registered handler and row 2 starts failing too — exactly the reproduction in Part 1, where
  nothing had been rooted and even reference types failed. Adding or removing an unrelated
  registration silently changes whether a different registration resolves.

The original brief predicted precisely this — "reference-type arguments survive on shared canonical
code, but a value-type response is the instantiation Native AOT hasn't generated." Part 1 appeared to
refute it, because with *nothing* rooted there is no canonical form either. Emitting closed calls
creates the canonical form, and the brief's prediction becomes correct.

**So the fallback must not be silent.** It is a JIT compatibility shim, and under AOT it has to be
off rather than partially working:

- Put the open-generic path behind a feature switch that is off when `PublishAot` is set, the shape
  `System.Text.Json` uses for its reflection fallback. `MakeGenericType` is then trimmed, IL3050 goes
  with it, and the failure becomes "not decorated" rather than "decorated on some machines".
- Document the narrowed contract plainly. Today a decorator covers anything in the collection when
  `ApplyDecorators` runs. Monomorphised, a generic decorator covers **what a module registered, in
  this compilation or a referenced one**. Hand-written `services.Add…` for a generic service is
  outside it.
- The generator cannot enumerate what it never saw, so there is no per-case diagnostic to emit. What
  it can report is the rule: a generic decorator exists, and registrations of that service made
  outside a module will not be covered.

This also retires the suggestion made earlier in this document that the package should emit both
shapes and let a dedup guard sort it out. The guard is still needed — see below — but it addresses
double decoration, not this.

### Double decoration is not hypothetical either

The same run shows it, in the JIT output, on the registration that *was* covered:

```
  [audit] CreateOrder -> OrderId
  [audit] CreateOrder -> OrderId
   resolved as LoggingBehavior`2
```

The closed call wrapped it, then the fallback wrapped it again. Two audit entries per request, and
the only symptom is duplicated side effects — no exception, nothing in the build. A guard keyed on
the descriptor and the decorator's generic type definition removes it.

### Why the closer beats the application writing `new` itself

Both shapes are AOT-safe. The closer wins on four counts that are not about performance:

| | application emits `new P.Behavior<X,Y>(…)` | package ships a closer |
|---|---|---|
| what `A` must read from `P` | the decorator's full constructor, from metadata | the open service type, and where the closer is |
| `internal` dependency in `P`'s constructor | **impossible** — `A` cannot name the type | fine, `P` resolves it itself |
| `P` changes its constructor | `A`'s emitted code is stale until rebuilt against the new shape | `P` owns it; `A`'s call site is unchanged |
| generic constraints on the decorator | checked at `A`'s call site | checked at `A`'s call site |

The second row is the one that decides it. A behaviour taking an internal logger, options type or
sink is ordinary, and it makes the direct-`new` shape unable to express a large class of real
decorators.

### How `A` finds out, and what it costs

`P`'s generator emits an assembly-level manifest alongside the closer:

```csharp
[assembly: ModuleDecorator(
    Service = typeof(IRequestHandler<,>),
    Closer = typeof(LoggingBehaviorRegistration),
    Order = 100)]
```

`A` reads `compilation.SourceModule.ReferencedAssemblySymbols`, calls `GetAttributes()` on each, and
matches the open service type against its own closed registrations. Measured on a 26-reference
compilation:

| | |
|---|---|
| read assembly-level attributes on every reference | **0.007 ms** |
| walk every public type in every reference (2,813 types) looking for `[Decorator]` | 0.257 ms |
| walk only references that themselves reference `DependencyModules.Runtime` | 0.017 ms |

Both are affordable, so cost is not what picks the manifest — 0.26 ms per keystroke would be
tolerable. **Note this does not contradict the 13 ms figure in
`convention-registration-and-decorators.md`.** That probe compared `AllInterfaces` on
`OriginalDefinition` and read `InstanceConstructors` for every type; this one reads attributes.
Assignability is the expensive query, not enumeration.

The manifest is chosen for what it *says*, not what it costs: it names the closer, the order and the
realm, so `A` never has to infer `P`'s intent from `P`'s type shape. Falling back to the filtered
type walk covers a package that has a `[Decorator]` but no manifest — an older version of the
generator, or a library that wrote the attribute by hand.

### Composition falls out

Each assembly monomorphises **its own** registrations against every manifest it can see. If package
`Q` also references `P` and registers handlers, `Q`'s generator emits `P`'s closer calls for `Q`'s
closed types. `A` does the same for `A`'s. The application composes both modules and both sets of
decorations run in the `ApplyDecorators` phase, ordered globally by the `Order` the manifest carried.

Nobody has to see anybody else's closed registrations, which is exactly the property that made the
runtime open-generic call attractive in the first place — recovered without reflection.

### What still has to be decided

- **Double decoration is now certain, not hypothetical.** If `A` and `Q` both register
  `IRequestHandler<Z, W>`, both emit a closer call naming that closed type, and `Decorate` wraps every
  matching descriptor — so both get wrapped twice. The dedup guard from Part 3, keyed on the
  descriptor and the decorator's **generic type definition**, is now required rather than merely
  advisable.
- **Backwards compatibility cuts both ways, and neither direction is free.** If `P` stops emitting
  the open-generic call, an application on an older generator loses the decoration with a green
  build. If `P` keeps emitting it, an AOT application gets the selective failure above. `P` emitting
  both is right for JIT and wrong for AOT, so the fallback has to be a feature switch rather than a
  decision `P` makes once at pack time.
- **Conditions and realms travel on the manifest**, and `A` emits the same `EnvironmentConditionWriter`
  guard around the closer call that it already emits around a local decorator.
- **A package with a generic `[Decorator]` and no generator** — a hand-written attribute, or a library
  that never adopted this — has no closer to call. That case gets the type walk, the direct-`new`
  emission, and a diagnostic when the constructor cannot be expressed from `A`.
- **Method naming.** `ApplyTo` by convention keeps the manifest to three values. Carrying the method
  name explicitly costs nothing and avoids a naming collision in a package with several decorators
  over one service; prefer the explicit form.

---

## Part 9: a runtime with no reflection

**Ian's rule, and it supersedes the feature-switch compromise in Part 8:** the runtime does no
reflection and no type closure by reflection. Not gated, not opt-out — absent.

### First, what is actually there

There is no `Reflection.Emit` anywhere in this repository, and never has been. The unsafe surface is
reflection *over* `Type`, and it is five call sites, all of them in one file:

| | |
|---|---|
| `DecoratorHelper.cs:91` | `decoratorType.MakeGenericType(...)` — type closure by reflection |
| `DecoratorHelper.cs:94` | `ActivatorUtilities.CreateInstance(provider, closedDecorator, inner)` — builds the **decorator** |
| `DecoratorHelper.cs:135,137` | `inner.GetType().GetInterfaces()` — exists only to feed `MakeGenericType` |
| `DecoratorHelper.cs:180` | `ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType)` — builds the **inner** |
| `DecoratorHelper.cs:198` | the same, keyed |

Interception has none: it is generated wrappers over typed interfaces. `DependencyRegistry` has none
either — its IL2067 is a missing annotation, not a reflective call.

So the whole rule reduces to rewriting one file. Four of the five call sites fall out of
monomorphisation. The fifth does not, and is the interesting part.

### The shape: the service is a type parameter, not a `Type`

```csharp
public static void Decorate<TService>(
    IServiceCollection services,
    Func<IServiceProvider, TService, TService> factory) where TService : class
```

Emitted as:

```csharp
DecoratorHelper.Decorate<IRequestHandler<CreateOrder, OrderId>>(services,
    (p, inner) => new LoggingHandler<CreateOrder, OrderId>(inner, p.GetRequiredService<IAuditSink>()));
```

Three things follow that are worth more than the reflection removal itself:

- **No casts.** The inner arrives typed, so the generated lambda has no `(IRequestHandler<…>)inner`.
- **`MakeGenericType` and `ResolveTypeArguments` have nowhere to live.** The type arguments are in the
  call site, written by the generator.
- **`GuardOpenGenericRegistration` becomes structurally impossible to violate.** `typeof(IRepo<>)`
  cannot be written as a type argument, so generated code cannot ask to decorate an open generic. The
  error class disappears rather than being reported at composition. What remains — a registration
  *made* as an open generic that a decorator wants to cover — the generator can see, and should say so
  as a build diagnostic.

### The hard case: producing the inner without constructing it

Decoration replaces a descriptor with a factory, so whatever the descriptor produced must still be
produced. An `ImplementationInstance` is returned and an `ImplementationFactory` is invoked — neither
reflects. An `ImplementationType` has to be **built**, and that is what `ActivatorUtilities` was for.

The way out is not to build it. **Displace the registration under a private key and let the container
build it, exactly as it would have if nothing had been decorated:**

```csharp
// [i] was ServiceDescriptor(IResource, implementationType: Resource, Scoped)
services.Add(new ServiceDescriptor(typeof(Resource), innerKey, typeof(Resource), Scoped));
services[i] = new ServiceDescriptor(typeof(IResource),
    p => factory(p, (IResource)p.GetRequiredKeyedService(typeof(Resource), innerKey)), Scoped);
```

**Be honest about what this achieves.** You cannot have a container that constructs types named by
`Type` without the container reflecting; `AddSingleton<IFoo, Foo>()` reflects. The achievable line is
not "zero reflection in the process" but:

> DependencyModules adds no reflection of its own. A decorated registration is constructed by exactly
> the same path as an undecorated one.

That line is checkable, and it is the one worth defending. Stacked decorators cost nothing extra —
after the first rewrite the descriptor is a factory, so the second decorator takes the
`ImplementationFactory` branch and only the innermost is ever displaced.

### Verified

Built with `IsAotCompatible=true`: **zero IL warnings in the new helper** (one IL2067 appeared first
and was closed by annotating the displaced `implementationType` — a pure annotation, no behaviour).
Published Native AOT and run:

```
shipped DecoratorHelper (ActivatorUtilities):
  FAILED: InvalidOperationException: A suitable constructor for type 'TracingResource'
          could not be located.
reflection-free helper (container-owned inner):
  resolved      : used (traced)
  inner disposed when the scope ended: yes

generic decoration through the reflection-free helper:
  reference-type response: LoggingHandler`2 -> OrderId { Value = abc }
  value-type response:     LoggingHandler`2 -> 42
```

Every remaining IL warning in that publish comes from the old `DecoratorHelper` still being
referenced. Delete it and the application publishes clean.

### It also fixes a disposal leak that has nothing to do with AOT

The third line above is not about AOT. Under **JIT**, today:

```
shipped DecoratorHelper (ActivatorUtilities):
  resolved      : used (traced)
  inner disposed when the scope ended: NO (0)
reflection-free helper (container-owned inner):
  inner disposed when the scope ended: yes
```

`ActivatorUtilities.CreateInstance` produces an object the container does not own, so it is never
registered for disposal. **Decorating a scoped `IDisposable` service silently leaks its disposal
today, on every runtime, for every user.** The displacement fixes it because the container creates
the inner and therefore disposes it.

This deserves its own test and arguably its own release note. It is the strongest argument in this
document that is not about AOT at all.

### What the rule costs

- **Descriptor count grows.** One extra keyed descriptor per decorated implementation-type
  registration. Tests asserting on `Services.Count` will need updating, and anything walking the
  collection sees the displaced entries.
- **The private key must be deterministic.** A static counter is wrong: it is not thread-safe and
  makes the collection differ between runs. Derive it from the decorated service type and the
  decorator identity, both of which the call site already has.
- **The type-driven `Decorate(IServiceCollection, Type, Type)` overload is deleted**, which is a
  binary break on a public API. At `1.0.0-rc` that is affordable. After 1.0 it is not, so the timing
  argues for doing this now rather than after the parity work.

### Where the rule does not apply

Worth stating so nobody over-applies it:

- **The analyzers.** They run inside the compiler and are never published. `ITypeDefinition`,
  `SymbolEqualityComparer` and everything else in Impl are unaffected.
- **The testing packages.** `DependencyModules.Moq`, `.NSubstitute`, `.FakeItEasy` and `.Testing` wrap
  libraries that genuinely do emit IL at run time. They never ship in a published application, and
  the rule would be meaningless there.

That second point resolves the loose end from Part 8. A coverage check — "a generic decorator exists,
and this registration of it was never decorated" — needs `IsGenericType` and
`GetGenericTypeDefinition`. Those are pure metadata reads with no trimming or AOT implication, but
they are still `Type` introspection. **Put the check in `DependencyModules.Testing` as an explicit
`VerifyDecoratorCoverage()`**, where reflection is already the house style and nothing ships. The
runtime package then holds none, and the silent gap is catchable by anyone who writes a composition
test.

---

## Part 10: sequencing

Ordered so each step ships on its own and makes the next cheaper.

| # | Work | Effort | Why here |
|---|---|---|---|
| 1 | `IsAotCompatible=true` on `DependencyModules.Runtime`, with the IL\* warnings promoted to errors | trivial | Nothing in this document would have shipped had this been on. It is what turns "no reflection" from a policy into a build failure |
| 2 | Rewrite `DecoratorHelper` per Part 9: `Decorate<TService>`, literal `new`, inner displaced under a private key. Delete the type-driven overload | moderate | Removes all five reflective call sites, fixes the disposal leak, and makes step 5 a smaller change. Binary-breaking, so it wants doing before 1.0 |
| 3 | Correct `website/guide/aot.md` and the status line of `convention-registration-and-decorators.md` | small | They currently promise something that does not work |
| 4 | Make the candidate transform syntax-only; key `LocationModel` on the identifier and drop its line/character half; cache the syntax-derived parts on the node | moderate | 11.3 → 1.7 ms per keystroke. Prerequisite for any honest merge discussion, and for recommending the extension seam |
| 5 | Monomorphise case A — attribute-registered closed generics | moderate | Single assembly. Needs the double-decoration guard and a diagnostic for what it cannot cover |
| 6 | Decide case B: merge, or emit from both packages with a dedup guard | — | Cheaper to decide after (4) |
| 7 | Emit a generic closer plus an assembly manifest for every generic `[Decorator]`, and consume manifests from referenced assemblies | larger | Part 8. The only shape that is both AOT-safe and cross-assembly, verified end to end. Needs the dedup guard from step 5 first |

Four things that should **not** be built:

- **An MSBuild property to gate the convention scan.** Measured above: it does not gate anything.
- **A runtime fallback to the open-generic call, in any form** — not gated, not feature-switched, not
  opt-out. Measured in Part 8: it succeeds for reference-type arguments on canonical code the closed
  calls happened to produce and fails for value-type ones, so whether a registration resolves depends
  on which *other* registrations exist. Part 9 removes it outright; what it covered becomes a
  documented contract and a `VerifyDecoratorCoverage()` in the testing package.
- **An intern table for `TypeDefinition`.** Measured 2.4× slower than allocating.
- **Fewer diagnostics.** They cost 0.05 ms per 2,000 and they are the differentiator. The eager
  location capture that feeds them is the cost, and it is fixed by step 4.

### Boundary cases still to scope

Recorded here rather than resolved, since they change what a diagnostic should say:

- `RegistrationFormOf`/`OpenFormOf` register a pass-through generic implementation as an **open**
  generic, and `GuardOpenGenericRegistration` then throws at composition. That throw should become a
  build diagnostic, conditional on a decorator actually existing for the service.
- Partially-open shapes — `class H<T> : IHandler<Order, T>` — return null from `RegistrationFormOf`
  and are dropped with nothing reported. Compare `Diagnostics.cs:48` in martinothamar/Mediator, whose
  `OpenGenericRequestHandler` is a warning and on by default.
- `[Decorate]` declared on a module is read from `ModuleEntryPointModel.AttributeModels`, so it is
  subject to the same single-compilation limit as `[Decorator]`.

---

## Reproducing any of this

- **Native AOT:** a console app with `PublishAot`, a generic `[Decorator]` over two handlers (one
  value-type response), a non-generic `[Decorator]`, and a plain `[SingletonService]` control.
  Publish and run it. On a machine whose Command Line Tools SDK is older than Xcode's, ILC's link
  step needs `<LinkerArg Include="-isysroot /Applications/Xcode.app/.../MacOSX.sdk" />` or it fails
  on `-ldl` before producing a binary.
- **Cross-assembly (Part 8):** two projects. A library holding the service interface, an open generic
  decorator, and a `static void ApplyTo<T1, T2>(IServiceCollection)` closer; an application that
  references it, declares handlers the library has never seen, and calls the closer with closed type
  arguments. Publish the application AOT and run. Build the library with `IsAotCompatible=true` to
  confirm the closer introduces no warnings of its own.
- **Analyzer warnings, no publish needed:**
  `dotnet build src/DependencyModules.Runtime -p:IsAotCompatible=true -p:TargetFramework=net10.0`
- **Generator timings:** extend `benchmarks/DependencyModules.Benchmarks` with counters in the
  predicate and transform, and construct the driver with
  `new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true)`
  to read `TrackedSteps`. Time only `RunGeneratorsAndUpdateCompilation`; one class per syntax tree;
  real method bodies. All three of those were already load-bearing in the existing benchmark and
  remain so.
