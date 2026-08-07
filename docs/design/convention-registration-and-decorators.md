# Design: convention registration and decorators

Status: part 2 phase A is implemented — typed decorators, module-level `[Decorate]`, open generic
decorators, and global ordering. Convention registration (part 1), generated forwarding (phase B),
and interceptors (part 3) remain design only.

Two features that would close the functional gap with [Scrutor](https://github.com/khellang/Scrutor)
without giving up what makes this library different: everything resolved at compile time, no
reflection, no assembly scanning at run time, trimming and Native AOT safe.

They are described together because they share machinery and because the ordering model has to be
agreed once for both.

- [Part 1: convention registration](#part-1-convention-registration)
- [Part 2: decorators](#part-2-decorators)
- [Part 3: interceptors](#part-3-interceptors)
- [Sequencing](#sequencing)

---

## Background: what already exists

Worth knowing before reading either part, because both build on it.

**The registration pipeline.** `BaseSourceGenerator` builds a `ModuleEntryPointModel` per module and
a `ServiceModel` per service. `ServiceSourceGenerator` combines them and hands them to
`DependencyFileWriter`, which emits `ModuleDependencies(IServiceCollection)`. The runtime applies
those through `DependencyRegistry<T>`.

**The decorator plumbing is already half-built and unused.** `DependencyRegistry` has a public
`AddDecorator(RegistryFunc)`, an `ApplyDecorators(IServiceCollection)`, and `LoadModules` already
runs decorators as a distinct phase after all services are registered:

```csharp
ApplyFeatures(serviceCollection, modules);
ApplyServices(serviceCollection, modules);      // every module's registrations
ApplyDecorators(serviceCollection, modules);    // then every module's decorators
```

`IDependencyModule.InternalApplyDecorators` exists as a default no-op. `ConfigureDecorators` is now
invoked; what remains missing is the generator half, since nothing emits an `InternalApplyDecorators`
override.

**This ordering is the feature, not an accident.** Decorators observe everything registered by every
module in the `AddModule(s)` call, so cross-module decoration works without the developer sequencing
anything. Scrutor requires `Decorate` to be called after the relevant `Add`, by hand.

**The contract to document:** a decorator sees the services registered by the modules in its
`AddModule(s)` call. Anything the application registers afterwards is outside that scope. This is
inherent to `IServiceCollection` — decoration rewrites descriptors, so it can only see descriptors
that exist. Scrutor has the same constraint.

---

## Part 1: convention registration

### Motivation

Scrutor's headline feature is assembly scanning:

```csharp
services.Scan(scan => scan
    .FromAssemblyOf<IRepository>()
    .AddClasses(c => c.AssignableTo<IRepository>())
    .AsImplementedInterfaces()
    .WithScopedLifetime());
```

The attributes in this library already solve the same problem at compile time. The gap is narrower
than it looks: developers who do not want to annotate every class individually.

**Do not port Scrutor's API.** A fluent scanning DSL means runtime reflection over assemblies, which
is exactly what this library exists to remove, and would give the package two registration
mechanisms with different trimming characteristics. The compile-time equivalent is a pattern
evaluated by the generator.

Note also that `IServiceCollectionConfiguration.ConfigureServices` already provides unrestricted
fluent access to `IServiceCollection` for anything a convention cannot express.

### Proposed API

```csharp
[DependencyModule]
[RegisterByConvention("*Repository", Lifetime = ServiceLifetime.Scoped)]
[RegisterByConvention("*Service", Lifetime = ServiceLifetime.Singleton, As = RegisterAs.ImplementedInterfaces)]
public partial class DataModule;
```

| Property | Meaning | Default |
|---|---|---|
| pattern (ctor arg) | glob over the type's **name**, or its fully qualified name when the pattern contains `.` | required |
| `Lifetime` | lifetime for matches | `Transient` |
| `As` | `ImplementedInterfaces`, `Self`, or `SelfAndInterfaces` | `ImplementedInterfaces` |
| `Using` | `RegistrationType` (`Add`, `Try`, `TryEnumerable`, `Replace`) | `Add` |
| `Realm` | scope the convention to a realm | none |

### Selector priority

Name globbing is the weakest of the available selectors and is listed last deliberately. The one
that carries most real usage is assignability:

```csharp
[RegisterByConvention(AssignableTo = typeof(IRequestHandler<,>), Lifetime = ServiceLifetime.Scoped)]
```

Ordered by value:

1. **`AssignableTo`**, including open generic interfaces. Handlers, validators, policies, strategies.
   Cheaper here than in Scrutor, because assignability is a symbol question at compile time rather
   than a reflection question at run time.
2. **`WithAttribute`** for marker attributes.
3. **Namespace**.
4. **Name glob**, last. It is the selector most likely to match something unintended when a class is
   added years later.

**Open generic closing is the make-or-break behaviour.** `CreateOrderHandler : IRequestHandler<CreateOrder, OrderId>`
must register the *closed* interface against the implementation. Emission for this is already proven:
a class closing an open generic registers and resolves correctly today, and only for its own type
argument. What is new is the selector, not the code generation.

### Explicitly out of scope: scanning referenced assemblies

Scrutor's `FromApplicationDependencies()` registers types from packages you do not own. That is
**not** planned, for two reasons:

- It contradicts the compile-time goal. It is the one Scrutor capability that has no honest
  compile-time equivalent.
- The module system already covers the case that actually matters. Cross-assembly scanning is largely
  a workaround for having no way to compose registrations across projects; here each project declares
  its own module and composes through module attributes.

If demand appears, a separate runtime-scanning package could provide it without compromising the
core. It should not live in this library.

### Glob semantics

Deliberately small. Two wildcards, no regex:

| Token | Matches |
|---|---|
| `*` | zero or more characters |
| `?` | exactly one character |

A pattern containing `.` matches against `Namespace.TypeName`; otherwise against the bare type name.
Matching is ordinal and case-sensitive, consistent with C# identifiers.

Implement by translating the glob to a `Regex` **once per pattern** at model construction, not per
candidate class. Anchor both ends.

### Where it goes in the pipeline

The convention lives on the module, but the classes it matches are separate syntax nodes, and an
incremental generator's predicate cannot see other providers. So candidate classes must flow through
their own provider and be filtered after the combine.

1. **New provider** for candidate types: predicate accepts any `ClassDeclarationSyntax` /
   `RecordDeclarationSyntax` that is `public` or `internal`, not `static`, not `abstract`, and
   carries **no** existing service attribute (an explicit attribute always wins).
2. **Transform** extracts a *syntax-only* `ConventionCandidateModel`: type name, namespace,
   base-list entries as written, modifiers, file path. It must not touch the semantic model.
3. **Combine** candidates with the module's conventions, then filter by glob.
4. **Resolve semantically only for survivors**, producing the same `ServiceModel` /
   `ServiceRegistrationModel` the attribute path produces.

From step 4 onward this is the existing pipeline. Convention registration should produce models
indistinguishable from attribute registration, so `DependencyFileWriter` needs no changes.

### Performance

Measured on this generator, in-process, excluding MSBuild overhead:

| Classes | Matching | Generator time |
|---|---|---|
| 500 | 0 | 53 ms |
| 500 | 500 | 167 ms |
| 2000 | 0 | 182 ms |
| 2000 | 2000 | 605 ms |

Consistently **~0.2 ms per class that reaches the transform**.

Three conclusions:

- **"Listening to all classes" is already what happens.** `CreateSyntaxProvider`'s predicate runs on
  every syntax node today. Conventions do not add a new category of work; they move classes from
  "rejected by the predicate" (near free) into "runs the transform" (~0.2 ms).
- **A convention matching everything in a 2000-class project costs roughly 0.4 s on a cold build.**
  Acceptable for a build. Not acceptable per keystroke, which is what incremental caching exists to
  prevent: after the first run only the edited node re-runs. This is why the model comparers matter
  and must stay correct.
- **Keep step 2 syntax-only.** Non-matching classes must never reach the semantic model. Return the
  existing `ServiceModel.Ignore` sentinel for non-matches, as the attribute path already does.

**Unrelated optimisation available.** Every provider here uses `CreateSyntaxProvider`. Roslyn 4.3+
offers `ForAttributeWithMetadataName`, which pre-indexes attribute usage and is substantially faster
for attribute-driven providers. Switching the existing attribute paths would reduce that 0.2 ms and
cleanly separate the two cost profiles: attributes become an indexed lookup, conventions remain a
full visit. Worth doing independently of this proposal.

### Conflicts and precedence

1. An explicit service attribute always wins; a class carrying one is never a convention candidate.
2. If several conventions in one module match a class, it is an error (DM0004) rather than a silent
   pick. Ambiguity here produces registrations nobody can predict from reading the source.
3. Conventions in *different* modules may both match; each module registers into its own realm, which
   is existing behaviour.
4. A convention matching zero types is a warning (DM0005). A typo in a pattern otherwise fails
   silently, which is the failure mode this codebase has repeatedly had to hunt down.

### Diagnostics

| ID | Severity | Condition |
|---|---|---|
| DM0004 | Error | Two conventions in one module match the same type |
| DM0005 | Warning | A convention matched no types |
| DM0006 | Warning | A convention matched a type that cannot be constructed, e.g. no accessible constructor |

Add to `DependencyModuleDiagnostics`, and record them in `AnalyzerReleases.Unshipped.md` or the
build fails RS2008.

### Test plan

Behavioural, using `GeneratedAssembly` — compile, load, resolve. Do not assert on generated text.

- each glob token, including no-wildcard exact match
- name-only versus fully-qualified patterns
- `As` options: interfaces, self, both
- lifetime and `Using` propagation
- explicit attribute beats convention
- abstract, static, and non-public types excluded
- DM0004/DM0005/DM0006 fire; a valid convention reports nothing
- incremental: editing an unrelated method body reuses cached output
  (`GeneratorTestHarness.RunIncremental`)

---

## Part 2: decorators

### Phase A: typed decorators

The smallest thing that closes the Scrutor gap. Estimated one day.

```csharp
public interface IRepository { Item Get(int id); }

[SingletonService]
public class Repository : IRepository { ... }

[Decorator(Order = 1)]
public class CachingRepository(IRepository inner, IMemoryCache cache) : IRepository {
    public Item Get(int id) => cache.GetOrCreate(id, _ => inner.Get(id))!;
}
```

Plus a module-level form for services you do not own:

```csharp
[DependencyModule]
[Decorate(typeof(IRepository), typeof(CachingRepository), Order = 1)]
public partial class DataModule;
```

**Not** an attribute on the interface. That inverts the dependency — the abstraction would name a
concrete implementation detail and could no longer be declared without referencing its decorators —
and it does not work for interfaces you do not own.

#### Implementation

1. New `DecoratorModel` (service type, decorator type, order, realm), produced by a provider keyed on
   `[Decorator]` and by reading `[Decorate]` from the module.
2. `DependencyFileWriter` gains a second emitted method, `ModuleDecorators(IServiceCollection)`,
   registered through the **existing** `DependencyRegistry<T>.AddDecorator`.
3. `DependencyModuleWriter` emits the `InternalApplyDecorators` override that nothing currently
   generates, calling `DependencyRegistry<T>.ApplyDecorators(services)`.
4. `DependencyRegistry.ApplyDecorators(collection, modules)` additionally invokes
   `IServiceCollectionConfiguration.ConfigureDecorators`, mirroring how `ApplyServices` invokes
   `ConfigureServices`.

Step 4 is two lines and fixes a public API that is currently a no-op, independently of the rest.

#### Emitted shape

Descriptor rewrite, the same approach Scrutor takes:

```csharp
private static void ModuleDecorators(IServiceCollection services) {
    for (var i = services.Count - 1; i >= 0; i--) {
        var descriptor = services[i];
        if (descriptor.ServiceType != typeof(global::App.IRepository)) continue;

        var inner = descriptor;   // capture before replacing
        services[i] = new ServiceDescriptor(
            typeof(global::App.IRepository),
            provider => new global::App.CachingRepository(
                (global::App.IRepository)Instantiate(provider, inner),
                provider.GetRequiredService<global::Microsoft.Extensions.Caching.Memory.IMemoryCache>()),
            descriptor.Lifetime);
    }
}
```

Points that matter:

- **Every** matching descriptor is decorated, not just the first, so multiple implementations behind
  one interface all get wrapped.
- The decorated descriptor keeps the **original lifetime**. A decorator must not silently change it.
- Iterate backwards; the list is mutated in place.
- Capture the original descriptor before replacing it, or the factory closes over its own replacement
  and recurses infinitely at resolve time. This is the single most likely bug in the whole feature
  and deserves a dedicated test.
- `Instantiate` handles the three descriptor shapes: implementation type, factory, and instance.

#### Ordering

**Implemented.** `DependencyRegistry.AddDecorator` takes an optional trailing `order`:

```csharp
public static int AddDecorator(RegistryFunc registryFunc, int order = 0)
```

Lower values are applied first and sit closer to the implementation; higher values wrap them.
`ApplyDecorators` sorts with a stable sort, so decorators sharing an order keep registration order
rather than nesting arbitrarily.

Design notes:

- **Optional and trailing, not required and leading.** `AddDecorator` is called by generated code, not
  by developers, so a required parameter would improve no one's ergonomics. It also keeps the change
  source-compatible, and matches `IDependencyModuleFeature<T>.Order`, which already defaults to 0 and
  is already sorted by `ApplyFeatures`.
- **Convention for composition across packages:** framework packages use `0-999`, application code
  uses `1000` and above, so an application's decorators wrap those contributed by its libraries.
- **Rejected: carrying the service type on `AddDecorator`.** It would allow runtime detection of
  colliding orders across packages, which cannot be caught at compile time because a package's
  decorators are already compiled when the application builds. Judged not worth the API complexity;
  the documented range convention solves the practical problem. Recorded here because adding it later
  is a breaking change.

With attributes there is **no natural order**. Source order is not meaningful, and partial classes
across files make it worse. `Logging(Caching(Repo))` and `Caching(Logging(Repo))` behave differently
on a cache hit, so this cannot be left implicit.

- `Order` is applied ascending; lower wraps closer to the implementation.
- Forcing an explicit order is **not** wanted. Scrutor, MediatR pipeline behaviours and ASP.NET
  middleware all use declaration order; attributes cannot, since order across files is not
  guaranteed. So the rule is: default to 0, and report **DM0007** when two decorators target the same
  service with *equal* order, which is precisely when the result would be unpredictable.
- `Order = 1` and `Order = 2` therefore need no ceremony, and a single decorator needs no `Order`.

#### Keyed services and generics

- Decorating `IRepository` does **not** decorate `[SingletonService(Key = "x")] IRepository` by
  default. Add `Key` to the attribute to target one, `Key = "*"` for all.
- Open generic decorators (`CachingRepository<T> : IRepository<T>`) are legal C# and should be
  supported; match the open generic descriptor by `ServiceType.GetGenericTypeDefinition()`.

#### Diagnostics

| ID | Severity | Condition |
|---|---|---|
| DM0007 | Error | Multiple decorators target one service and `Order` is ambiguous |
| DM0008 | Error | Decorator does not implement the service type it decorates |
| DM0009 | Warning | Decorator targets a service no module registers |
| DM0010 | Error | Decorator has no constructor parameter of the decorated type |

### Phase B: generated forwarding

Removes the boilerplate of writing a decorator that overrides one member and forwards the rest.

```csharp
[Decorator]
public partial class CachingRepository(IRepository inner, IMemoryCache cache) : IRepository {
    public Item Get(int id) => cache.GetOrCreate(id, _ => inner.Get(id))!;
    // every other IRepository member is generated, forwarding to inner
}
```

Estimated four to six times phase A. The algorithm is roughly 150 lines; the work is the member
matrix.

#### The two APIs that make it tractable

**`ITypeSymbol.FindImplementationForInterfaceMember`** answers "did the developer write this?" At
generation time the partial is incomplete, so it returns null exactly for the members that need
forwarding. No name matching, no heuristics.

**`SymbolDisplayFormat` renders the signature.** Never hand-roll this. Configure one format with
`IncludeParameters | IncludeRef | IncludeDefaultValue | IncludeTypeConstraints |
IncludeNullableReferenceTypeModifier` and Roslyn renders generics, constraints, `ref`/`out`/`in`,
default values, `params`, tuple element names and nullability — it is the authority on its own
syntax. The body is then mechanical.

#### Caching constraint

Symbols are not equatable and must never be stored in a model. **Render signatures to strings during
the syntax transform** and store those. This satisfies fidelity and incremental caching in one move.

#### Architectural requirement

**Separate signature rendering from body emission.** Phase C reuses the signature half with a
different body. If phase B hardcodes `=> inner.Foo(...)` throughout, phase C is a rewrite rather than
an addition. This costs nothing to honour now and is the main reason to read parts 2 and 3 together.

#### Member matrix

| Shape | Handling |
|---|---|
| Methods, properties, generics, constraints | `SymbolDisplayFormat` |
| `ref` / `out` / `in`, `params`, default values | same format; repeat the ref kind at the call site |
| Nullable annotations | format flag |
| Events, indexers | separate emission shapes |
| Members inherited from base interfaces | walk `AllInterfaces`, not just `GetMembers()` |
| Two interfaces with colliding members | emit explicit implementations |
| `static abstract` members | **refuse, DM0011** |
| `ref` returns | **refuse, DM0011** |

The last two rows are the safety valve. For any shape not supported, emit a clear diagnostic and
generate nothing. The failure mode must be "DependencyModules does not support X" and never a `CS`
error inside generated code.

#### Test plan

`GeneratedAssembly` is the right tool: declare an interface with an awkward shape, implement one
member, assert the others actually reach `inner`. Each case executes rather than matching text. Cover
every row above, and confirm DM0011 fires for the refused shapes.

---

## Part 3: interceptors

### The constraint that forces the design

This is illegal C#:

```csharp
public class LoggingDecorator<T> : T   // a type parameter cannot be a base type
```

A hand-written decorator can therefore only target a specific interface, or a generic one. One class
can never wrap `IFoo`, `IBar` and `IBaz`. **A generic interceptor must be generated per service** —
there is no other route.

Which is why this belongs with phase B: it is the same forwarding machinery with a different body
template.

| Feature | Generated body |
|---|---|
| Phase B decorator | `=> inner.Foo(a, b);` |
| Interceptor | `=> pipeline.Run(() => inner.Foo(a, b));` |

Castle DynamicProxy emits IL at run time and does not work under Native AOT. A compile-time
interceptor does. That is a stronger differentiator than Scrutor parity.

### Two tiers

**Tier 1, lifecycle hooks.** No boxing, no argument array.

```csharp
public interface IInterceptor<TService> {
    void OnEnter(string member);
    void OnExit(string member);
    void OnError(string member, Exception exception);
}
```

The wrapper calls `OnEnter`, invokes `inner.Foo(a, b)` directly, then `OnExit`. Covers logging,
metrics, tracing and timing — realistically most interceptor use. It is cheap *because* it is
compile-time; a runtime proxy cannot do this without reifying the call, because it does not know the
signature.

**Tier 2, full invocation.** Opt-in, for caching, retry and authorisation.

```csharp
public interface IInvocationInterceptor {
    ValueTask<object?> InterceptAsync(IInvocation invocation);   // arguments, ProceedAsync, short-circuit
}
```

Costs boxing of value-type arguments and returns, plus an array per call. Making it explicitly
opt-in keeps the fast path fast and keeps faith with the library's performance claims.

### Async

The hardest part, and where a generator has a structural advantage. `Task`, `Task<T>`, `ValueTask`,
`ValueTask<T>`, `IAsyncEnumerable<T>` and synchronous methods each need a different wrapper shape.
Castle handles this so poorly that `AsyncInterceptorBase` exists as a community workaround. A
generator knows the return type at compile time and emits the correct shape per method with no
runtime type inspection.

### Restrictions

- **Interfaces only.** Class interception needs virtual members and inheritance; silently doing
  nothing on a non-virtual method is a classic AOP trap. Diagnose instead.
- **`ref` / `out` parameters** cannot round-trip through `object[]`; refuse in tier 2. Tier 1 handles
  them, since it does not reify.
- Decorators and interceptors both produce wrappers around the same service and therefore **share one
  ordering model**.

---

## Designed for a future mediator package

The decorator work is the foundation for a possible `DependencyModules.Mediator`, which raises its
priority: a pipeline behaviour *is* a decorator, so building the mediator first would mean
implementing the pipeline mechanism twice.

**The cross-package guarantee already holds**, and is intentional rather than incidental:

```
ApplyServices(all modules)     // the application registers its handlers
ApplyDecorators(all modules)   // then every module's decorators run
```

Because decorators run in a separate phase *after* all services, a decorator declared in a referenced
package wraps handlers registered by the consuming application, regardless of module order.
Registrations cross assembly boundaries at run time through `DependencyRegistry<TModule>`, so a
package genuinely can contribute decorations to an application's services. This is exactly the
semantics `IPipelineBehavior` needs.

What a mediator would require from this design, all of which it now has:

| Requirement | Status |
|---|---|
| Open generic decoration of `IRequestHandler<,>` | Phase A, primary scenario |
| A package decorating the consuming application's services | Already works, verified |
| Ordering that composes across packages | `order` parameter plus the range convention |
| Short-circuiting, for validation failures | Falls out: do not call `inner` |
| Decorator receiving its own dependencies | Falls out: normal constructor injection |

The differentiator for such a package would not be feature parity with MediatR, which went commercial
in July 2025. It would be **compile-time verification** — a missing handler reported as a build error
rather than a production exception — and direct dispatch with no reflection. That is the same shape
as the community's move from AutoMapper to source-generator alternatives.

It should ship as a **separate package** depending on the core, never fused into it.

## Sequencing

Each step is independently shippable and makes the next cheaper.

| Step | Effort | Notes |
|---|---|---|
| `AddDecorator` order parameter | done | Implemented; source-compatible |
| Global decorator ordering | done | `InternalGetDecorators` collects across modules; sorted together |
| `DecoratorHelper` descriptor rewrite | done | All three descriptor shapes, open generics, keyed, lifetime preserved |
| `[Decorator]` / `[Decorate]` attributes | done | Runtime surface only; the generator does not read them yet |
| Generator emission for decorators | done | `[Decorator]` and `[Decorate]` are read and emitted; DM0007 for ambiguous order |
| Phase B: generated forwarding | remaining | Opt-in via `partial`; additive |
| Interceptors | remaining | Reuses the same descriptor rewrite |
| Wire `ConfigureDecorators` | done | Was a public no-op; now invoked after all services are registered |
| Convention registration | days | Independent of decorators; can proceed in parallel |
| Phase A, typed decorators | ~1 day | Closes the Scrutor gap. Emit the body through a template |
| Phase B, generated forwarding | 4–6× A | Only worth it if C is plausible; keep signature and body separate |
| Tier 1 interceptors | moderate | Reuses B's signature rendering |
| Tier 2 interceptors | moderate | Only if demand appears |

Nothing beyond the first row is 1.0 work. The one decision that is live now is the architectural
constraint in phase B: keep signature rendering separate from body emission.
