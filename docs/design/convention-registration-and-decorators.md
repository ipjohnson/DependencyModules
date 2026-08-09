# Design: convention registration and decorators

Status: convention registration (part 1) is implemented and unshipped, in its own analyzer package.
Part 2 phase A is implemented — typed decorators, module-level `[Decorate]`, open generic decorators,
and global ordering. Interceptors (part 3) are implemented, and the shipped model is the pipeline
described in `docs/HANDOFF.md` rather than the two tiers sketched here. Generated forwarding
(phase B) remains design only.

Part 1 now also carries a **plan for scanning referenced assemblies**, which earlier revisions of
this document ruled out. That reversal is recorded in *Scanning referenced assemblies* below,
together with the probe that produced it.

Two features that would close the functional gap with [Scrutor](https://github.com/khellang/Scrutor)
without giving up what makes this library different: everything resolved at compile time, no
reflection, no assembly scanning at run time, trimming and Native AOT safe.

They are described together because they share machinery and because the ordering model has to be
agreed once for both.

- [Part 1: convention registration](#part-1-convention-registration)
  - [What shipped](#what-shipped)
  - [Scanning referenced assemblies — planned](#scanning-referenced-assemblies--planned)
- [Scrutor parity](#scrutor-parity)
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

The attributes in this library already solve the same problem at compile time, for developers willing
to annotate every class individually. Everything else Scrutor offers is measured against what shipped
in *Scrutor parity* below — the gap is wider than "annotation avoidance", and almost none of it is
about other assemblies.

**Do not port Scrutor's API.** A scanning DSL *evaluated at run time* means reflection over
assemblies, which is exactly what this library exists to remove, and would give the package two
registration mechanisms with different trimming characteristics. The compile-time equivalent is the
same declaration **read** by the generator rather than executed — which is what shipped, and what
makes scanning a referenced assembly a compile-time operation rather than a runtime one.

Note also that `IServiceCollectionConfiguration.ConfigureServices` already provides unrestricted
fluent access to `IServiceCollection` for anything a convention cannot express.

### Proposed API — superseded

Kept for the property table below, which still describes options worth having. The declaration
shape it proposes was **not** what shipped; see *Declaration site* immediately after.

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

### Declaration site — settled, do not re-open

Conventions are declared as a **fluent chain in a method body read at compile time**, not as
attributes:

```csharp
[DependencyModule]
public partial class DataModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll<IRepository>().AsScoped();
    }
}
```

**Ian's call, made after the alternatives below were investigated and compiled.** The fluent form
carries more options than an attribute argument list comfortably can, extends without breaking
existing declarations, and is a pit of success: IntelliSense on `conventions.` enumerates what is
available, and the chain refuses to compile when it does not typecheck. That outweighs the two costs
— the interface name appearing twice, and a method body that is never executed.

**Why the interface name is in the method definition, and why it stays.** `IConventionDefinitions`
is emitted `internal`, so an implicit `public void Conventions(...)` is `CS0051: Inconsistent
accessibility`. Explicit implementation is the only shape that satisfies the interface. Making the
contracts `public` to escape it is worse on both counts that matter: they join the consumer's public
API surface, and two assemblies that each emit them and reference each other produce **CS0436**
conflicts — measured, three warnings in the referencing project.

Rejected, each compiled on net8.0 at 0 warnings before being turned down:

| shape | why not |
|---|---|
| `[Conventions]` marker on a freely named `private static` method, no interface | Works, and would be `ForAttributeWithMetadataName`-indexed. Loses nothing but the interface name — and gains a silent failure: forget the attribute and the method is merely unused. |
| `RegisterAll<T>` generic attributes on the module, no method | The superseded proposal above. Retires the never-executed body, but an attribute argument list is the thing the fluent chain was chosen over. |
| `partial void Conventions(...)`, generator emits the defining declaration | Emission is currently skipped when nothing matches; a missing declaration is `CS0759` pointing at generated code, which is the failure mode this codebase avoids. Verified. |
| interface with a default implementation + ordinary private method | Half a fix — the interface is still in the base list — and a missing method silently compiles where it is `CS0535` today. |

One incidental point for the shipped shape: explicit implementations cannot be static, so they are
the only form CA1822 does not flag. Every alternative needs `static` on the method to stay clean
under `AnalysisMode=All`.

### What shipped

Four verbs, and that is the entire API:

```csharp
conventions.RegisterAll<IRepository>().AsScoped();
conventions.RegisterAll(typeof(IHandler<,>)).AsTransient().IncludeBaseClasses();
```

`RegisterAll<T>()` / `RegisterAll(typeof(...))`, the three `As*` lifetimes, and `IncludeBaseClasses()`.
**One selector axis — assignability — and one registration shape, "as the interface that matched."**
Everything in *Selector priority* below except assignability is unimplemented, and the *Glob
semantics* section describes a selector that does not exist yet.

Three structural decisions worth not re-deriving:

- **`DependencyModules.Conventions` is its own analyzer package**, sharing `Impl` by compiling in its
  sources rather than by declaring a second `[Generator]` there. A project that never references it
  never loads the class-scanning provider — which matters, because that predicate is the one thing
  here that visits every type node.
- **Matching runs at output time, not in a transform.** That is why it can report diagnostics at all,
  and why it does not have to be cacheable. The transform stays syntax-shaped and the expensive part
  stays out of the incremental graph.
- **It produces `ServiceModel`/`ServiceRegistrationModel` indistinguishable from the attribute path**,
  so `DependencyFileWriter` needed no changes. That seam is what makes the parity work below cheap.

`ConventionCandidateUtility` deliberately does not materialise the transitive interface closure: only
interfaces written on the declaration plus what those extend, with base-class-reached interfaces held
in a separate list behind `IncludeBaseClasses()`. The cached model stays proportional to what was
written rather than to hierarchy depth. The opt-in is also stricter than Scrutor's `AssignableTo`,
which always walks base classes and silently enrols every subclass added later.

### Known defect: partial classes produce a false DM0004

**Proven by running it.** A partial class that reaches the scanned interface from more than one part
is reported as ambiguous and registers nothing:

```
error DM0004: 'Foo' is matched by more than one convention in 'TestModule'
              — as 'IFoo' and as 'IFoo'. Narrow one of them, or move it to another module.
```

One convention, named twice. The candidate provider is per *declaration* — `CreateSyntaxProvider`
over `ClassDeclarationSyntax` — while `RemoveAmbiguous` groups by `ImplementationType` and assumes one
model per type. Two syntax declarations of one partial type yield two `ConventionCandidateModel`s with
equal `ImplementationType`.

The shape that will actually hit users is not the duplicated interface in the reproduction. It is
`partial class Foo : IFoo` in one file and `partial class Foo : FooBase` in another under
`IncludeBaseClasses()`, where both parts reach `IFoo`.

Fix by merging candidates on `ImplementationType` before matching, or by deduping on the
`(convention, candidate)` pair inside `RemoveAmbiguous`. Note this fails loudly rather than silently,
which is the better failure mode, but the registration is still lost.

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

### Scanning referenced assemblies — planned

**This reverses an earlier position in this document.** The previous revision ruled out all
cross-assembly scanning on the grounds that it "contradicts the compile-time goal" and is "the one
Scrutor capability that has no honest compile-time equivalent." That conflated three different
things, and it is true of only one of them.

Scrutor's eight source methods collapse into three capabilities:

| | Capability | Compile-time answer |
|---|---|---|
| 1 | Types in the compilation being built | what ships today |
| 2 | Types in assemblies **referenced at compile time** — project references and NuGet packages | **yes — proven, see below** |
| 3 | Assemblies discovered at run time from the deps file or from disk | none, ever |

Only (3) is genuinely out of reach, and it is also the part of Scrutor that already fails under
trimming and Native AOT. So the gap this library has against Scrutor is not "cannot see other
assemblies"; it is "cannot see assemblies that were not there at build time."

#### Verified, do not re-derive

Probed end to end with a standalone Roslyn harness, not reasoned about. A library was compiled to a
real DLL, then a **second compilation referencing it and containing no handlers in any syntax tree**
was run through an `IIncrementalGenerator`, which emitted:

```csharp
private static void ModuleDependencies(IServiceCollection services) {
    services.AddScoped(typeof(global::TheLibrary.IHandler<global::TheLibrary.CreateOrder, global::TheLibrary.OrderId>), typeof(global::TheLibrary.CreateOrderHandler));
    services.AddScoped(typeof(global::TheLibrary.IHandler<global::TheLibrary.RenameOrder, global::TheLibrary.OrderId>), typeof(global::TheLibrary.RenameOrderHandler));
}
```

That is the whole convention feature working across an assembly boundary: an open generic matched
against metadata, each implementation registered against the **closed** construction it actually
implements, constructors read from metadata, and two exclusions applied correctly — an `internal`
handler, invisible across the boundary, and one whose only constructor is private.

Reproduce it by compiling a library to an in-memory DLL, referencing it from a second
`CSharpCompilation` whose only syntax tree declares a module, and running a generator that resolves
`compilation.GetAssemblyOrModuleSymbol(reference)` and walks `IAssemblySymbol.GlobalNamespace`.
Assignability is `type.AllInterfaces` compared on `OriginalDefinition`; constructors are
`type.InstanceConstructors`. The probe was standalone and is not retained.

**This is what makes it AOT-safe rather than AOT-hostile.** Scrutor enumerates an assembly's types by
reflection, which the trimmer cannot follow: it has no way to know those types are needed, removes
them, and the scan finds nothing at run time. Doing the same work at compile time emits a literal
`typeof()` for every implementation into the consumer's assembly. That is a static reference the
trimmer roots, and — the part that actually matters — it lets the
`[DynamicallyAccessedMembers(PublicConstructors)]` annotation on `ServiceDescriptor`'s
implementation-type parameter flow to a known type, so the constructor survives too. Neither works
when the type is only discovered at run time. **The capability that breaks Scrutor under AOT is the
capability that works here.**

#### Cost, measured

The naive shape — walk every referenced assembly — is not viable, and the numbers say so:

| | types visited | time |
|---|---|---|
| Every referenced assembly | 5,350 | 13 ms |
| One assembly named by the convention | 10 | 0.019 ms |

Roughly 700× apart, on the same keystroke. And 13 ms is a *minimal* eleven-reference compilation;
a real application carries an order of magnitude more references, so the naive path only gets worse.

**Correction to an assumption that was wrong.** `context.MetadataReferencesProvider` does *not* buy
per-reference caching. It must be combined with `CompilationProvider` to resolve
`GetAssemblyOrModuleSymbol`, and the compilation changes on every keystroke, so every reference is
re-visited on every edit — measured, all eleven. What bounds the cost is not the provider shape. It is
the convention naming the assembly, so a reference is rejected on its name before any symbol work
happens.

#### The API this forces

```csharp
conventions.RegisterAll(typeof(IHandler<,>)).InAssemblyOf<SomeTypeInThatPackage>().AsScoped();
```

- **The assembly is named, always.** There is no `InAllDependencies()` and there should never be one.
  This is deliberately narrower than Scrutor, and the measurement above is the reason.
- **`InAssemblyOf<T>()` rather than a string name.** A type argument cannot be written unless the
  assembly is already referenced, so "you named an assembly that is not referenced" becomes
  impossible to express rather than a diagnostic to report. Prefer the shape that deletes the error.
- **Absent the call, a convention scans the compilation being built** — existing behaviour, unchanged.

#### Implementation constraints

1. **Model equality is not optional here.** `ImmutableArray<T>` compares by reference of the
   underlying array, so an unchanged walk still re-runs everything downstream — measured. The result
   has to be wrapped the way `ModelEquality.ListEquals` already does it elsewhere in this codebase.
2. **Visibility differs by reach.** Referenced assemblies expose only `public` types; the
   in-compilation path also takes `internal`. One verb with two reaches has to be documented, because
   nothing can detect the `internal` type it cannot see.
3. **Constructor info needs a symbol-driven path.** `ServiceModelUtility.GetConstructorInfo` is
   syntax-driven. Metadata types need an equivalent that reads `IMethodSymbol`. This is the one
   genuinely new piece of code, and the probe confirms the information is all there.
4. **DM0010 loses its location.** "Registered as `IFoo`, reported at the class" is the best affordance
   conventions have, and there is no class to squiggle inside a referenced DLL. Matches from a
   referenced assembly have to report at the `RegisterAll` line instead.
5. **No new diagnostic IDs are required.** DM0004, DM0005, DM0006, DM0009 and DM0010 all generalise
   unchanged.

#### What stays out, and why the module system still comes first

`FromApplicationDependencies()`, `FromDependencyContext()` and `FromAssemblyDependencies(Assembly)`
load runtime libraries by name, including assemblies that were never compile-time references. There
is no compile-time answer and no way to fake one. If demand ever appears, a separate runtime-scanning
package could provide it; it should not live in this library.

Note also that (2) is not the first tool to reach for. Any referenced project **you own** is better
served by declaring its own module with its own conventions and composing through module attributes —
explicit, ordered, and cross-assembly by construction. Referenced-assembly scanning earns its keep on
assemblies you **do not** own, where you cannot add a module: third-party packages whose handlers,
validators or policies you want registered.

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

Shipped, and the reason this feature is worth having over a runtime scanner:

| ID | Severity | Condition |
|---|---|---|
| DM0004 | Error | Two conventions in one module match the same type |
| DM0005 | Warning | A convention matched no types |
| DM0006 | Warning | A convention matched a type that cannot be constructed, e.g. no accessible constructor |
| DM0009 | Error | A convention declaration could not be read |
| DM0010 | Info | A service is registered by convention, reported at the class |

**Canonical allocation across the whole library** — the tables in parts 2 and 3 below predate several
of these and are wrong where they disagree. Next free ID is **DM0011**.

| ID | Severity | Meaning |
|---|---|---|
| DM0001 | Error | The generator failed; registrations may be missing |
| DM0002 | Warning | A service type cannot be constructed and was not registered |
| DM0003 | Error | A module marked `[DependencyModule]` is not partial |
| DM0004 | Error | Two conventions in one module match the same type |
| DM0005 | Warning | A convention matched no types |
| DM0006 | Warning | A convention matched a type with no accessible constructor |
| DM0007 | Error | Two decorators of one service share an order |
| DM0008 | Warning | A service marked for interception cannot be wrapped |
| DM0009 | Error | A convention declaration could not be read |
| DM0010 | Info | A service is registered by convention |

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

## Scrutor parity

Scrutor's surface, read off its interfaces rather than its README, is three axes and a strategy:

| Axis | Scrutor |
|---|---|
| Source | `FromEntryAssembly`, `FromAssemblyOf<T>`, `FromAssembliesOf`, `FromAssemblies`, `FromApplicationDependencies(+predicate)`, `FromAssemblyDependencies(Assembly)`, `FromDependencyContext(+predicate)`, `AddTypes` |
| Filter | `AssignableTo`, `AssignableToAny`, `WithAttribute`×3, `WithoutAttribute`×3, `InNamespaceOf`, `InNamespaces`, `InExactNamespaces`, `NotInNamespaceOf`, `NotInNamespaces`, `Where(Func<Type,bool>)` |
| Shape | `AsSelf`, `As<T>`, `As(Type[])`, `AsImplementedInterfaces(+predicate)`, `AsSelfWithInterfaces(+predicate)`, `AsMatchingInterface(+action)`, `As(Func<Type,IEnumerable<Type>>)`, `UsingAttributes` |
| Lifetime / strategy | `WithSingleton/Scoped/TransientLifetime`, `WithLifetime(ServiceLifetime)`, `WithLifetime(Func<Type,ServiceLifetime>)`, `WithServiceKey(object \| Func)`, `RegistrationStrategy.Skip/Append/Throw/Replace(ReplacementBehavior)` |

### The mapping

**Have it, sometimes better.**

| Scrutor | Here |
|---|---|
| `AssignableTo`, including open generics | `RegisterAll` — and cheaper, assignability being a symbol question at compile time |
| base-class reach inside `AssignableTo` | `IncludeBaseClasses()`, opt-in rather than always-on |
| `UsingAttributes` | the attribute path, which is this library's *primary* API rather than a scanning afterthought |
| `WithSingleton/Scoped/TransientLifetime`, `WithLifetime(ServiceLifetime)` | `AsSingleton`/`AsScoped`/`AsTransient` |
| `RegistrationStrategy.Throw` | becomes a **build error**, not a runtime exception |

**Missing, fully buildable at compile time, no philosophical cost.** This is the bulk of the gap, and
none of it involves other assemblies:

| Scrutor | Notes |
|---|---|
| `AsSelf`, `AsSelfWithInterfaces`, `As<T>`, `As(Type[])` | **The largest single hole.** A concrete type with no interface cannot be registered by convention at all today — `ConventionCandidateUtility.IsCandidate` rejects anything with no base list |
| `AsImplementedInterfaces` proper | `FirstMatchingInterface` returns one interface per candidate per convention. `ServiceModel.Registrations` is already a list, so multi-registration is emission-ready |
| `InNamespaceOf`, `InNamespaces`, `InExactNamespaces`, `NotIn*` | Syntax-only. The cheapest thing on this list |
| `WithAttribute<T>`, `WithoutAttribute<T>` | Syntax, and `ForAttributeWithMetadataName`-indexable |
| `WithAttribute<T>(predicate)` | Partial — literal argument matching yes, arbitrary lambda no |
| name globbing | Semantics already specified in *Glob semantics* above; never implemented |
| `AsMatchingInterface` (`Foo` → `IFoo`) | A symbol lookup |
| `AssignableToAny` | An overload, or repeated `RegisterAll` |
| `WithServiceKey(object)` | **`ServiceRegistrationModel.Key` already exists**; the matcher passes null |
| `RegistrationStrategy.Skip/Append/Replace` | **`ServiceRegistrationModel.RegistrationType` already exists**; the matcher passes null |
| `FromAssemblyOf<T>` against a referenced assembly | *Scanning referenced assemblies*, above |

**No compile-time equivalent — every lambda-taking overload.** `Where(Func<Type,bool>)`,
`AsImplementedInterfaces(predicate)`, `As(Func<Type,IEnumerable<Type>>)`,
`WithLifetime(Func<Type,ServiceLifetime>)`, `WithServiceKey(Func<Type,object?>)`. A generator cannot
run user code over types it is describing. **The escape hatch already exists and should be named in
the documentation rather than left to be discovered:** `IServiceCollectionConfiguration.ConfigureServices`
gives unrestricted fluent access to `IServiceCollection` for anything a convention cannot express.

**Runtime-only, will not be built.** `FromApplicationDependencies`, `FromDependencyContext`,
`FromAssemblyDependencies`. See *What stays out* above.

### Plan, in order

Ordered by value per unit of work. Everything above the line is in-compilation and needs no new
pipeline shape.

| # | Work | Notes |
|---|---|---|
| 1 | Fix the partial-class DM0004 | Defect, loses registrations today |
| 2 | `AsSelf()` / `AsSelfWithInterfaces()` | Needs `IsCandidate` to stop requiring a base list |
| 3 | Namespace filters | Syntax-only, cheapest on the list |
| 4 | Multi-interface registration | `Registrations` is already a list |
| 5 | `RegistrationType` and `Key` pass-through | Both fields already exist and are being passed null |
| 6 | Attribute filters (`WithAttribute` / `WithoutAttribute`) | FAWMN-indexable |
| 7 | `AsMatchingInterface`, `As<T>`, name globbing | Rounds out the shape axis |
| | — | |
| 8 | Scanning referenced assemblies | New pipeline shape; see the constraints above |

**Sequencing note.** Steps 2 and 4 change what a `ServiceModel` may contain, and step 8 changes where
candidates come from. Doing 8 first would mean building the referenced-assembly path against a
one-interface-per-candidate model and then reworking it. Keep 8 last.

### What Scrutor structurally cannot do

Worth holding on to, because it is the actual competitive position and none of it is about feature
count:

- **DM0005 answers Scrutor's most common failure.** A renamed interface or a typo'd filter registers
  zero services, the build stays green, and the application throws at resolve time. Scrutor cannot
  report it: its scan does not run until startup, by which point the build is long over.
- **DM0006 catches an unconstructable match at build time**, where Scrutor surfaces it as an
  `ActivatorUtilities` failure at resolve.
- **DM0004 refuses ambiguity**, where Scrutor silently appends or last-wins.
- **DM0010 reports provenance at the class**: "this type is in the container as `IFoo`, via
  `IAuditedFoo`." This is the standing complaint about assembly scanning — *where did this
  registration come from* — answered in the IDE, at the type, with no tooling.
- **Zero startup cost, no reflection, trimming and Native AOT safe**, including for referenced
  assemblies once the plan above lands.

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

**Stale — only DM0007 shipped as written.** DM0008, DM0009 and DM0010 were subsequently allocated to
interception and to convention registration; see the canonical allocation in part 1. The three
decorator conditions below are still worth reporting, but they need IDs from DM0011 upward.

| ID | Severity | Condition |
|---|---|---|
| DM0007 | Error | Multiple decorators target one service and `Order` is ambiguous — **shipped** |
| ~~DM0008~~ | Error | Decorator does not implement the service type it decorates — *needs a new ID* |
| ~~DM0009~~ | Warning | Decorator targets a service no module registers — *needs a new ID* |
| ~~DM0010~~ | Error | Decorator has no constructor parameter of the decorated type — *needs a new ID* |

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

> **Superseded where it disagrees with what shipped.** Interception is implemented, and the two-tier
> hook model below (`OnEnter`/`OnExit`/`OnError`, plus an opt-in `IInvocation`) was **rejected** in
> favour of a pipeline in which one `Intercept` method receives the call and decides whether to
> `Proceed()`. `docs/HANDOFF.md` records the shipped model and the reasoning. The section below is
> kept for *The constraint that forces the design* and *Async*, which still hold.

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

**Done.**

| Step | Notes |
|---|---|
| `AddDecorator` order parameter | Source-compatible; optional and trailing |
| Global decorator ordering | `InternalGetDecorators` collects across modules; sorted together |
| `DecoratorHelper` descriptor rewrite | All three descriptor shapes, open generics, keyed, lifetime preserved |
| `[Decorator]` / `[Decorate]` attributes | Runtime surface and generator emission; DM0007 for ambiguous order |
| Wire `ConfigureDecorators` | Was a public no-op; now invoked after all services are registered |
| Convention registration | Own analyzer package. Assignability axis only — see *What shipped* |
| Interceptors | Shipped as a pipeline, not the two tiers in part 3. See `docs/HANDOFF.md` |

**Remaining**, most valuable first. Steps 1–7 are the Scrutor parity plan and are detailed above.

| Step | Effort | Notes |
|---|---|---|
| Partial-class DM0004 fix | small | Defect; loses registrations today |
| Parity steps 2–7 | days | In-compilation, no new pipeline shape; several are pass-through of fields that already exist |
| Scanning referenced assemblies | moderate | Proven feasible. New pipeline shape; keep it after the parity steps |
| Phase B, generated forwarding | 4–6× phase A | Only worth it if the member matrix is worth it; keep signature and body separate |

The one architectural constraint still live: in phase B, keep signature rendering separate from body
emission.
