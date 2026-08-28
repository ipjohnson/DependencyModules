# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.2.0] - 2026-08-28

Four applications built against the published 1.1.0 packages, by four agents who did not read each
other's work. Every fix 1.1.0 shipped held up under runtime assertion. What did not hold up was
everything around them — a test integration that reported a pass having run nothing, a package that
could not resolve against the xUnit a new user installs, and three statements this project published
that were wrong.

**Upgrade note: one behaviour changes.** A `[Mock]` parameter now overrides a `[TestExport]` naming
the same service. A test declaring both silently held the real implementation before; it holds the
mock now. On the same method that pair is contradictory and reports `DM0021`; from a class or an
assembly it is the default being overridden for one test, which is what having both scopes is for.
See [Mocking frameworks](https://ipjohnson.github.io/DependencyModules/guide/testing-mocking#precedence).

Three new diagnostics arrive, all warnings, so `TreatWarningsAsErrors` may turn a green build red on
code that was quietly doing nothing. Every one of them can be silenced where it is written — which
is itself new, and the subject of the first entry below.

Separately from the round, generation moves to CSharpAuthor 2.0 and gains a `GeneratedCodeStyle`
property choosing the brace style of generated files. Generated code changes in mechanical ways —
no method body is different — and the *Changed* entries carry the details.

### Fixed

- **`.editorconfig` and `#pragma warning disable` did not silence most diagnostics, and 1.1.0 said
  they did.** A diagnostic carried its file and line but was not attached to the syntax tree, and
  Roslyn decides both `.editorconfig` severity and `#pragma` filtering from the tree. Only `NoWarn`
  and `WarningsAsErrors` worked.

  1.1.0 shipped a *documentation correction* claiming `.editorconfig` worked, "including for the
  error-severity codes". It worked for `DM0016` and `DM0019` and nothing else — and 1.1.0 is what
  broke it, by relocating ten diagnostics from the project onto the declaration they are about. The
  relocation is what put them on a location with no tree.

  All three mechanisms now reach every code. Diagnostics are reported from their own source outputs,
  which is what lets them see the compilation without re-emitting every file on every keystroke.

  There was no `.editorconfig` anywhere in this repository and no test exercised any of the three
  mechanisms, which is how a documented behaviour shipped wrong in opposite directions across two
  releases. There is now one test per code.

- **`DependencyModules.xUnit` could not resolve against the `xunit.v3` a new user installs.** The
  dependency was declared with no upper bound, `dotnet add package xunit.v3` resolves 4.0.0, and
  4.0.0 removed the discovery API the compiled discoverer binds to. Every test failed at discovery
  with a `MissingMethodException` naming an xUnit internal rather than this package, and nothing
  warned at restore or build.

  Bounded to `[3.2.2, 4.0.0)`, so NuGet reports it at restore instead. `DependencyModules.NUnit` is
  bounded to its major for the same reason, before it can do the same thing.

- **`[MemberData]` under `[ModuleTest]` produced zero test cases and the run reported a pass.**
  `[MemberData]` resolves its member against `MemberType`, which xUnit back-fills on a path this
  integration does not take — and left null, it returns an *empty* row collection rather than
  throwing. A project moving a row set from `[InlineData]` to `[MemberData]` lost that coverage
  silently. `[MemberData(…, MemberType = typeof(X))]` was the shape that worked.

  Returning no tests is now a failure rather than a pass, which is what xUnit's own delay-enumerated
  theory does and what the NUnit half of this integration already did.

- **`DependencyModules_GenerateFactories` undid the 1.1.0 interception fixes.** Applying an
  interception means finding the one registration its wrapper was generated from, and a factory
  registration cannot say what implementation it built. Under the property the filter matched
  nothing and interception went back to wrapping every registration of the service type — an
  unmarked sibling came back inside another class's wrapper, and interceptors ran once per
  registration. The property the AOT guidance recommends turning on restored the bug the release
  shipped to fix.

  A service any implementation intercepts now keeps its `typeof` registration whatever the property
  says. By service type rather than by implementation: an *unmarked* sibling built by a factory
  cannot identify itself either, so exempting only the marked class left exactly that case broken.

- **A realm-scoped service with an unrealmed `[Intercept]` was never intercepted.** The registration
  landed only in the named module and the applicator landed everywhere except it — dead in every
  container that could exist, reported by nothing, because each half individually followed the rule.
  An interception naming no realm now takes the one its own class's service attribute names.

  Filed twice, by different agents against different applications, as two separate defects. It is
  one bug.

- **`DM0016` and `DM0019` never fired for a module from a referenced package**, which is the case
  both exist for. They were built only from the modules declared in the same compilation, so a
  module arriving from a NuGet package was invisible to them — and with no local module at all, the
  pass returned before `DM0019` was considered. The reference page printed exactly the shape that
  did not reproduce.

- **`[Intercept]` was matched by its written name**, so a namespace-qualified, `global::`-qualified
  or aliased usage found nothing — no wrapper, no diagnostic, and a cross-cutting concern that had
  simply stopped running. This is the defect 1.1.0 fixed for the service attributes and did not
  carry across. Nobody filed it; it was found reading the code beside the ones that were.

- **A private property on a module produced generated code that would not compile.** Module
  parameters were chosen by "settable and not static" without ever looking at accessibility, so a
  private property was copied onto the generated `public` attribute — `CS0122`, twice, with no
  diagnostic explaining it. A property the attribute cannot reach is no longer a parameter, and
  `DM0018` no longer reports one.

- **Declaring `[DependencyModule] partial class ApplicationModule` in a project that already gets one
  generated produced `CS8785` and then `CS0311` against the developer's own type.** The two models
  had different namespaces at the point they were grouped, so consolidation never saw them as one
  module and both were emitted under the same file name. Getting Started tells the reader to use that
  name. They merge now.

- **`InterceptorModelComparer` omitted `Realm`**, so editing only `Realm = typeof(X)` hit the
  incremental cache and left the applicator on whichever module had it before.

### Added

- **`[Intercept(Lifetime = …)]`.** Interceptors were always registered as singletons, so one taking a
  scoped dependency became a captive dependency — silent unless `ValidateScopes` is on, and
  `GenerateFactories` switches that off. Still `TryAdd`, so an interceptor carrying its own service
  attribute keeps that lifetime.

- **`[Intercept(Members = …)]`.** Covering every member is right for auditing or retry and wrong for
  an interface with properties, where a timing interceptor records a call per read. Takes `Methods`,
  `Properties`, `Indexers`, `Events` or any combination. A member left out is still forwarded; it
  just does not run through the chain.

- **`[Decorator(Implementation = …)]`.** The inverse of the change 1.1.0 made to interception. A
  decorator wraps every registration of its service by default — that is what separates it from an
  interceptor — but a project with several implementations of one interface had no way to say "wrap
  this one".

- **`Order` on the service attributes.** Decorators and interceptors have had one since they existed.
  Registrations did not, so several implementations of one interface arrived in the order the
  generator emitted them, sorted by class name — renaming a class reordered a pipeline. Everything
  defaults to `0` and the sort is stable within one order.

- **A `GeneratedCodeStyle` MSBuild property** choosing the brace style of every generated file:
  `Allman` (the default, and what every prior version emitted) or `KAndR`. The name carries no
  `DependencyModules_` prefix on purpose — it is shared with other source generators, so one csproj
  line styles all of them. An unrecognized value falls back to `Allman`.

- **`DM0020`** reports an interception no module applies, so it can never run. The case it catches is
  a realm-only module registering the class by convention, where the realm is decided at match time
  and an interception cannot inherit it.

- **`DM0021`** reports `[Mock]` and `[TestExport]` naming one service on the same method, where the
  parameter wins and the `[TestExport]` beside it does nothing.

- **`DM0022`** reports a decorator naming an implementation in a project emitting factories, where it
  would wrap all of them instead of one.

### Changed

- **CSharpAuthor 1.1.1010 → 2.0.0-preview1004.** Generated files change in two mechanical ways:
  attributes are now written `global::`-qualified, so a type a consumer adds later can never
  collide with them, and the `using` directives 1.x derived from already-qualified type references
  are gone. Method bodies are unchanged. The upgrade also surfaced — and this release fixes — a
  double-quoting bug in string-array attribute arguments, which 1.x rendered as `""a""`: not valid
  C#, and never caught because nothing compiled that path.

- **`InterceptorFileWriter.Write` takes the configuration model as a fourth parameter** (in the
  source-shipped `DependencyModules.SourceGenerator.Impl` seam), so the wrapper file honors
  `GeneratedCodeStyle`. A framework calling it directly passes its configuration through.

- **The interceptor wrapper's self-reference is built with its real namespace**, and its closed-over
  type parameters as `TypeParameterDefinition`, instead of empty-namespace `TypeDefinition`s that
  leaned on CSharpAuthor rendering them bare. Under the published package the only visible change
  is a fully qualified self-reference in wrapper files; under the upcoming CSharpAuthor fix that
  qualifies global-namespace types in `Global` mode (its migration note B13), the old spelling
  would have rendered `global::Worker_Intercepted` for a type that lives in the module's
  namespace.

### Documentation

- **The *Composing modules* example doubled if you copied it into one project.** It declared both
  modules together while the prose meant two assemblies, and the warning explaining the doubling sat
  ninety lines lower under another heading. The example shows two projects now, the warning sits
  beside it, and the remedy that did not work — "have one compose the other" — is corrected: only a
  realm removes the doubling.

- **`[Intercept]`'s `Realm` existed only in the 1.1.0 changelog.** It, per-implementation
  interception, interceptor registration and lifetime, and the new `Members` are all on
  [Interception](https://ipjohnson.github.io/DependencyModules/guide/interception) now. The
  decorator-versus-interceptor table read as the opposite of the truth and has been corrected.

- **A new [runtime interfaces](https://ipjohnson.github.io/DependencyModules/reference/interfaces)
  page.** `DependencyModules.Runtime.Interfaces` appeared in samples and in the `CS0311` a colliding
  `ApplicationModule` produces, and was named on no page.

- **`IEnumerable<T>` injection and `[FromKeyedServices]` are documented**, along with the limit that
  keyed registrations are not in the enumeration.

- **`DM0016`–`DM0019` are cross-referenced from the guide pages that teach their shapes**, and the
  Parameters section carries the module-identity paragraph `DM0018` links into.

- **xUnit v3 is stated concretely**: `dotnet new xunit` gives v2, which cannot be used at all; the
  supported range is `[3.2.2, 4.0.0)`; and the guide's own data-driven example trips `xUnit1037`.

- **The `EmitCompilerGeneratedFiles` hazard is written out**, including the
  `<Compile Remove="generated/**" />` that the redirected-output form needs.

### Unversioned

Two design notes record what was deliberately not built: `docs/design/dispatch-by-name.md` and
`docs/design/runtime-module-graph.md`. Both are public surface that cannot be taken back inside 1.x,
and both need a decision rather than a guess. Dispatch-by-name is the strongest signal the round
produced — two applications hand-built the same per-request dictionary, neither author knowing about
the other.

Two of the round's findings were **withdrawn as correct-by-design** after review, and both came from
behaviour the documentation did not state. Four readers of one documentation set are not four
independent observers: where the text is silent they infer the same wrong thing. Both gaps are now
written down.

## [1.1.0] - 2026-08-26

Four registrations that silently were not what they said they were, and three new diagnostics. Every
fix here came out of building four applications against 1.0.0 — a CLI, a worker daemon, an Avalonia
desktop app and a Native AOT tool — and each one is the same shape: a clean build, no diagnostic, and
a program that quietly did the wrong thing.

A minor rather than a patch. Nothing in the public API breaks, but two members are added and three
diagnostics arrive, and both of those are minor-version events.

**Upgrade note: two of the new codes are errors, on code that compiled before.** `DM0017` and
`DM0019` report shapes that built green and registered nothing, so no working program breaks — but a
build carrying one goes red on upgrade. That is the fix arriving rather than a regression. Both can be
silenced with `NoWarn` or `.editorconfig` if you need to stage the work.

**Also worth reading if you rely on interception:** an interceptor now applies only to the
implementation it was declared on. If a sibling implementation of the same interface was being
intercepted, that stops — it was never asked for.

### Fixed

- **A namespace-qualified or aliased service attribute registered with the wrong lifetime.** Every
  legal spelling of `[SingletonService]` was discovered, but the lifetime was read back from how the
  attribute was *written* — so only a spelling literally starting with `Singleton` or `Scoped`
  produced that lifetime, and everything else fell through to `Transient`.

  | Written as | Before | Now |
  |---|---|---|
  | `[SingletonService]` | Singleton | Singleton |
  | `[SingletonServiceAttribute]` | Singleton | Singleton |
  | `[DependencyModules.Runtime.Attributes.SingletonService]` | **Transient** | Singleton |
  | `[global::…SingletonServiceAttribute]` | **Transient** | Singleton |
  | `using Svc = …SingletonServiceAttribute;` → `[Svc]` | **Transient** | Singleton |
  | `using SingletonAlias = …;` → `[SingletonAlias]` | Singleton | Singleton |

  The last two rows are the same attribute type aliased twice, behaving differently on the spelling of
  the alias. `As` and `Realm` were honoured throughout; only the lifetime was lost, which is what made
  it hard to see — a stateful singleton silently becoming per-resolve is a correctness bug rather than
  a performance one.

  The attribute is resolved through the semantic model to decide it matched at all, so the fix costs
  nothing extra: the resolved type is now carried to the lifetime rather than discarded.

- **An interceptor was applied to every implementation of the interface.** A wrapper is generated from
  one class and forwards that class's members, but it was applied to every registration of the service
  type — because interception reuses the decorator rewrite, and wrapping everything behind an interface
  is right for a decorator and wrong for this.

  An implementation carrying no `[Intercept]` came back wrapped in another class's wrapper. With both
  implementations marked, each registration was wrapped once per generated wrapper and every
  interceptor ran twice per call: doubled metrics, audit rows and retries, with nothing thrown.

  `DecoratorHelper.Decorate` gains an overload naming the implementation, and the generator uses it.
  Decoration is unchanged — a decorator still wraps every registration of its service.

- **`OnlyRealm = true` did not filter interceptors.** Services and decorators were filtered correctly;
  the interception pass ignored realms entirely, so a module built to be isolated emitted applicators
  for every `[Intercept]` in the assembly. Where the leaked interceptor's dependencies did not resolve
  in that container it threw while building the provider; where they did, it silently wrapped a service
  the module had never heard of. `[Intercept]` now takes a `Realm`, matching `[Decorator]`.

- **A module's property defaults were overwritten by a composition that did not name them.** The
  generated attribute assigned every property unconditionally unless it was declared nullable, so
  `public string Label { get; set; } = "default";` became `null` — surfacing as a
  `NullReferenceException` inside the module's own `ConfigureServices`. Nullability is an annotation
  rather than a runtime fact, so the guard is now emitted for every property.

  A value-typed parameter is unchanged and cannot be fixed this way: `int` is `0` until assigned and
  `0` is a legitimate value, so nothing can tell "not supplied" from the default. `DM0018` covers the
  case where that matters.

- **Ten diagnostics reported at the project rather than at the declaration.** `DM0002`, `DM0003`,
  `DM0007`, `DM0008`, `DM0009`, `DM0011`, `DM0012`, `DM0013`, `DM0014` and `DM0015` printed as bare
  `CSC : warning DM00xx:` with no file, line or column, so the IDE had nowhere to take you and the type
  name in the message was the only handle. They now report at the declaration. The remaining two
  location-less sites are the `DM0001` generator-failure handlers, where an exception escaped and there
  genuinely is no location.

- **`DM0005` recommended a call you had already made.** Its advice was a fixed tail appended whatever
  the cause, so a convention naming a *class* as the service type — which can never match, since
  conventions match through declared interfaces — was told to call `IncludeBaseClasses()`. That is the
  shape `dotnet new avalonia.mvvm` ships, and the shape every MVVM project starts from. The advice is
  now chosen by cause.

### Added

- **`DM0017`** (Error) reports a module declared inside another type. The generator completes a module
  at namespace level, so a nested one produced a second, detached type of the same name while the
  nested declaration never implemented `IDependencyModule` — documented as always wrong in the README
  and two guides, and reported by none of them.

- **`DM0018`** reports a module with parameters relying on the generated `Equals`/`GetHashCode`,
  which compares by type alone — so two instances carrying different values count as the same module,
  the first one reached wins, and the other is discarded with nothing said. Load a
  `[CacheModule(SizeLimit = 10)]` feature and a `[CacheModule(SizeLimit = 999)]` feature together and
  one `CacheSettings` arrives, not two.

  The generator has to choose an identity either way and picks type-only. This reports that it chose,
  so the choice becomes the developer's — and both answers are legitimate, whether that is "identity
  is the values" or "identity is the type, and I meant it". Declaring either suppresses the generated
  pair, which has always worked.

  Only **settable, non-static** properties count. A read-only property is not a parameter: a module
  implementing an interface with `public string Value => "A";` has nothing to configure and is not
  reported. The three parameterised modules in this repository's own integration tests now declare
  their identity rather than being exempted, one of each kind.

- **`DM0019`** (Error) reports an assembly-level module attribute outside the entry point file. Those
  are composed into the generated `ApplicationModule`, which is built from one compilation unit, so
  written anywhere else the attribute was read by nobody. It stays quiet when nothing generated an
  `ApplicationModule` — a class library, or a test project, where the test integration reads assembly
  attributes at run time and a file of their own is exactly right.

- **`[Intercept].Realm`**, matching `[Decorator].Realm`.

- **`DecoratorHelper.Decorate<TService>(services, decoratorIdentity, decoratorFactory, implementationType)`**,
  a new overload. The three-argument one is untouched, so generated code from 1.0.0 still binds.

### Documentation

- **The diagnostics reference was wrong about `.editorconfig`.** It said
  `dotnet_diagnostic.DM0005.severity = none` had no effect. It does work, including for the
  error-severity codes; what `.editorconfig` cannot do is *raise* a severity, because a generator's
  diagnostics arrive with theirs already fixed. Both halves are now stated.

- **`DM0010` and `DM0011` were described as never appearing in `dotnet build` at any verbosity.** They
  appear at `-v detailed`.

- **`DM0012` described the opposite of what the generator emits.** A condition with nothing to test
  cannot be false, so the guard is `if (true)` and the service registers *unconditionally* — the page
  said it never registers.

- `DM0017`, `DM0018` and `DM0019` are documented on the diagnostics reference.

### Packaging

The assembly version stays at `1.0.0.0` and is now pinned there for the whole of 1.x. It is part of
assembly identity, so moving it every minor release means a library compiled against `1.0.0.0` no
longer matches what a consuming application resolves. `FileVersion` and `AssemblyInformationalVersion`
carry the real version, and the package version is what anyone actually depends on.

### Unversioned

`DependencyModules.SourceGenerator.Impl` ships generator extension points as source and sits outside
the versioning promise, as [1.0.0](#100---2026-08-16) set out. It changed: `LocationModel` moved from
`DependencyModules.Conventions.Models` to `DependencyModules.SourceGenerator.Impl.Models` so the
service, interceptor, decorator and module models can all carry one, and `ModuleEntryPointModel`'s
constructor takes a `LocationModel` as its third argument. Anything built on `.Impl` will need both.

## [1.0.0] - 2026-08-16

The first stable release. Identical in content to `1.0.0-rc9340` below — it is the same commit
without the prerelease label, published so that `dotnet add package DependencyModules.Runtime`
resolves without `--prerelease`, which is the single most common thing to have gone wrong for someone
trying this library for the first time.

**What stability means here.** The public API is now under [semantic
versioning](https://semver.org/spec/v2.0.0.html): the attributes, the convention contracts, the
runtime interfaces and the testing attributes will not break within 1.x. Two things are deliberately
outside that promise, and both say so in their own documentation — the generator extension points
that `DependencyModules.SourceGenerator.Impl` ships as source, which exist for frameworks building
their own generator on this one, and the exact text of generated code, which is an implementation
detail rather than something to compile against.

Diagnostics are covered too, in the sense that a `DM####` code will not be reused for a different
meaning. New ones may be added in a minor release, and a new warning can turn a green build red under
`TreatWarningsAsErrors` — that is the normal cost of a diagnostic arriving, and `NoWarn` takes them
per-project.

The breaking changes gathered in the release candidates land here rather than after, which is the
point of doing them now: `DecoratorExpansion.Expand` changed shape, and `[InjectValues]` is
restricted to parameters. Both are described below.

`net8.0` and `net10.0` are supported. `net8.0` leaves support in November 2026 and will be dropped in
a later minor release, not a patch.

## [1.0.0-rc9340] - 2026-08-16

Everything since `1.0.0-rc9230`. The theme is registrations that were silently not happening: an
attribute the generator declined to recognise, a `Replace` that depended on how a class was named, a
mock that replaced the wrong slot, and three shapes it refused without saying so. One of those
refusals turned out to be unnecessary, and generic services can now be intercepted.

The other half came from building five applications against the released package and writing down
everything that got in the way — which turned up a generated file that could break a consumer's build
on an ordinary signature, an attribute that did nothing where it was written, and several documented
examples that did not compile.

Still a release candidate. `DecoratorExpansion.Expand` changed shape, but only on the generator
extension points, which are documented as unversioned.

**Upgrade note:** four new warnings, and one thing that will stop compiling. A project building with
`TreatWarningsAsErrors` may go red on work that was previously green and quietly not doing anything —
which is the point of them, but it is a build break rather than a nudge. `NoWarn` takes them
per-project; note that `.editorconfig` does not (see below). Separately, `[InjectValues]` is now
restricted to parameters, so a usage anywhere else is `CS0592` where it used to compile and be
ignored. Both are cases where the build going red is the fix arriving, not a regression.

### Added

- **Three diagnostics for shapes the generator used to refuse without saying so.** The first two share
  a constraint: decoration and cross-wiring replace a registration with a factory, and the container
  refuses a factory for an open generic service type — ``Open generic service type 'IRepository`1[T]'
  requires registering an open generic implementation type``. The third is about an interceptor that
  does not run.

  **`DM0013`** reports a decorator whose service is registered as an open generic. It covers all
  three shapes of the mistake, which until now failed in two different ways. A *generic* decorator
  is expanded against the closed constructions a compilation registers; an open generic registration
  closes nothing, so the expansion produced no decorations and the declaration was dropped in
  silence — a decorator sitting in the source, a green build, and nothing wrapping anything. A
  *non-generic* decorator named against an unbound service needed no expansion at all, so nothing
  caught it: it reached emission carrying `IStore<>` and produced `Decorate<IStore<>>`, which is
  CS7003 inside generated code. Reported whichever way the decorator was declared, on the class or
  on the module with `[Decorate]`.

  A decorator naming a service the compilation does **not** register stays quiet. Naming a service
  someone else registers is what `[Decorate]` exists for, so reporting there would fire on the
  feature's primary use.

  **`DM0014`** reports `[CrossWireService]` on a generic type. Cross-wiring shares one instance
  across the implementation and every interface it declares, which is emitted as a factory per
  interface. Registering each interface to the same open generic implementation type would compile
  and is a different contract — one instance per service type, the opposite of what the attribute
  promises — so it is refused rather than quietly substituted. The whole registration is dropped
  rather than the cross-wired half, because keeping the implementation's own registration would
  leave the instance unreachable through any of its interfaces.

  **`DM0015`** reports an interceptor that is quietly absent from some of the members it was applied
  to. Three interfaces cover the member shapes and the generator picks per member, so an interceptor
  implementing none of the one a member needs was simply left out of that member's chain. An
  argument-rewriting interceptor stopped rewriting; read as an authorisation or audit gate, it was a
  service that quietly was not gated. The sharpest form — an `IInterceptor` applied to a service whose
  members are all async, where it never ran at all — was invisible even to the generator, which
  discarded the model before anything could report on it. Reported once per interceptor and member
  shape, so a wide interface produces one line rather than forty.

- **A generic service can be intercepted.** A generic implementation registers as an open generic, and
  it was refused outright — because *decoration* cannot touch one, and interception inherited that
  constraint without needing it. Decoration rewrites a registration into a factory, which an open
  generic service type cannot carry; interception generates a type, and an open generic implementation
  type is what the container does accept.

  ```csharp
  [SingletonService]
  [Intercept(typeof(TracingInterceptor))]
  public class Repository<T> : IRepository<T> { … }
  ```

  The wrapper is generic over the same parameters — `Repository_Intercepted<T> : IRepository<T>` — and
  takes `Repository<T>` by its own type rather than the service, which would resolve back to the
  wrapper and recurse. `DecoratorHelper.InterceptOpenGeneric` swaps the registration and registers the
  implementation alongside it, carrying the lifetime and the service key across.

  Constraints come along with the parameters: `Repository<T> where T : class, IEntity, new()` is
  wrapped by `Repository_Intercepted<T> : IRepository<T> where T : class, IEntity, new()`, without
  which the wrapper could not reference what it wraps. `struct` and `unmanaged` already guarantee a
  default constructor and Roslyn reports one for them, so `new()` is dropped rather than repeated —
  writing it out is CS0451.

  Worth knowing before relying on it under Native AOT, and true of open generic registrations
  generally rather than of interception: a published binary closes them over reference types only.
  Measured on `osx-arm64`, `IRepository<Order>` resolves and `IRepository<int>` throws
  `Unable to create a generic service … because 'System.Int32' is a ValueType`. A plain
  `[SingletonService]` on a generic class behaves identically, which the AOT guide now says.

- **`DM0016`, for an assembly-level module attribute whose namespace nothing imports.** A module
  generates its attribute in the module's own namespace, and an assembly attribute has no namespace
  context to inherit — a `using` inside a namespace declaration cannot reach it, because assembly
  attributes precede every namespace in the file. So `[assembly: ApplicationModule]` without the
  import fails with `CS0246` naming `ApplicationModuleAttribute`: a type the developer never wrote,
  generated into a namespace the error does not mention. Every part of that message points away from
  the one-line fix, and both the testing guide and this README used to show the shape without it.

  Alone among these it is read from syntax rather than from the semantic model, and has to be — the
  attribute is written by the generator that is running, so it does not exist in the compilation
  being examined and nothing about it resolves. The question it can answer is "is there a module by
  this name, and could this file see it", which is why it stays quiet for an attribute matching no
  module in the compilation, a module in the global namespace, a usage already written qualified, and
  a namespace a `global using` supplies from any file.

### Fixed

- **An attribute the generator declined to recognise, depending on how it was spelled.** Attribute
  usages were compared as written — the type's simple name, and that name with `Attribute` appended —
  so every other legal spelling missed, and missing meant the registration was silently absent: no
  diagnostic, a green build, and a failure at the first resolve.

  | Written as | Before | Now |
  |---|---|---|
  | `[SingletonService]` | registered | registered |
  | `[DependencyModules.Runtime.Attributes.SingletonService]` | **skipped** | registered |
  | `[global::DependencyModules.Runtime.Attributes.SingletonServiceAttribute]` | **skipped** | registered |
  | `[DmAttrs.SingletonService]` (namespace alias) | **skipped** | registered |
  | `[DmSingleton]` (type alias) | **skipped** | registered |

  Service attributes are now resolved through the semantic model rather than string-matched, which is
  what makes an alias and a qualified name mean the same thing. Module attributes are matched on the
  name the usage ends in, so every qualified form works there too; a `using` alias of a *module*
  attribute is still not seen, because a predicate that must stay syntax-only cannot resolve one — and
  that case fails as a `CS0311` at `AddModule<T>()` rather than silently.

- **`Using = Replace` and `Using = Try` decided by the alphabet.** Registrations within a module are
  emitted sorted by implementation type name, and both act *on* a registration that has to already be
  there. Named so that the sort put them first, they ran before their target existed: `Replace`
  replaced nothing, added itself, and was then beaten by the very registration it meant to displace.

  ```csharp
  [SingletonService(Using = RegistrationType.Replace)] public class AaaThing : IThing;
  [SingletonService] public class ZzzThing : IThing;
  // asked for AaaThing; got [AaaThing, ZzzThing], and ZzzThing won
  ```

  They are now emitted after the plain `Add` registrations in their group, the same rule that already
  put conditional registrations last so the override pattern works. Renaming the class was the
  previous workaround, and nothing said you needed it.

- **`[Mock]` ignored `[FromKeyedServices]` on the same parameter.** The double was registered
  unkeyed, leaving the keyed registration — the one a consumer injects — untouched. The service under
  test kept the real implementation while the test held a double it believed was wired in: the
  arrangement ran, the double recorded nothing, and the assertion failed somewhere else entirely. The
  key on the parameter is now the key the double is registered under, and read back from. Identical
  under NSubstitute, Moq and FakeItEasy.

- **The generator copied 50 of its own source files into consuming projects' output.**
  `CopyToOutputDirectory` on a `Compile` item copies the *source*, and the metadata flowed to every
  project referencing the analyzer — 428KB of generator internals in `bin` and in `publish`. It
  affected `ProjectReference` consumers and this repository's own `benchmarks/` and test output; the
  NuGet package, which ships only `analyzers/dotnet/cs` and `build/`, was never affected.

- **Generated code that did not compile.** `[CrossWireService]` on a generic type leaked the type
  parameter into the registration as `typeof(ILedger<T>)`, with no `T` in scope, beside
  `GetRequiredService<Ledger<>>()` — CS0246 and CS7003, in a file the developer did not write. A
  non-generic decorator named against an open generic service produced CS7003 and CS1503 the same
  way. Both are now `DM0014` and `DM0013`.

- **A nullable type argument in a service type broke the build, and the consumer could not fix it.**
  A registered service type carries whatever nullable annotation its declaration used, so
  `class GetBookHandler : IHandler<GetBook, Book?>` emits `typeof(…Book?)`. Roslyn requires generated
  code to open a nullable context explicitly however the consuming project is configured, and the
  registrations file was the one generated file that never did — the module, attribute and interceptor
  writers all already called `EnableNullable`.

  The result was `CS8669` on a find-by-id handler, which is about as ordinary a shape as exists: a
  warning nobody could silence from their own source without dropping the annotation from their own
  domain signatures, and a hard failure under `TreatWarningsAsErrors`. It reproduced on the attribute
  and the convention path alike, because the convention path emits through the same writer — which is
  also why one call fixes both files.

  The annotation is still emitted. It is inert inside a `typeof`, since `typeof(Book?)` and
  `typeof(Book)` are one runtime type, and removing it would mean changing type modelling that
  decoration and interception depend on: `ConstructorArgumentWriter` reads nullability to choose
  `GetService` over `GetRequiredService`, and an interceptor wrapper needs the annotations to keep
  implementing what it wraps. A decorator declared against `IStore<Document>` still matches a
  registration of `IStore<Document?>`, so the difference is cosmetic rather than a silent miss.

### Changed

- **Breaking, and deliberately so: `[InjectValues]` is restricted to parameters.** It was the only
  one of the three testing attributes without an `AttributeUsage` — `[Mock]` is pinned to parameters
  and `[TestExport]` to methods — so writing it on a test method compiled, was never read, and then
  failed inside `ActivatorUtilities` with *"Multiple constructors accepting all given argument types
  have been found in type 'System.String'"*, naming neither the parameter nor the mistake. It is now
  `CS0592` at the attribute itself. Code that was silently doing nothing will stop compiling, which
  is the point.

- **`CSharpAuthor` 1.1.1010**, for `AddConstraint`. A `where` clause was previously assembled as a
  string and assigned to `WhereStatement`, which put C#'s ordering rules — one primary constraint
  first, `new()` last — in this generator. Two places needed them once a class could carry constraints
  as well as a method, and two copies of a subtle rule drift. `TypeParameterReader` reads a parameter
  once for both, and the library puts the parts in order.

- **`DM0008` now says what it costs.** One member the wrapper cannot override means *no* wrapper is
  generated, so every other member on the interface goes uninterceped too — and the guide read as
  though only the offending member did. A reader who fixed the named member and rebuilt would then
  meet the next one. The message and the interception guide both say so now.

- **Documentation corrections, each with a reproduction behind it.** The README's duplicate-module
  example compared `module.someString` against a primary constructor parameter, which is captured
  rather than a member — `CS1061`, and it was the only documented way to load a module more than
  once. `ExcludeGeneratedCodeFromCoverage` was documented with a `DependencyModules_` prefix it does
  not have. Getting started did not mention that a console app or class library needs
  `Microsoft.Extensions.DependencyInjection` for `ServiceCollection` and `BuildServiceProvider`.
  `[Decorator]`'s `Realm` property was undocumented.

- **`DM####` diagnostics cannot be tuned through `.editorconfig`, and the reference said they could.**
  They are reported by a source generator rather than an analyzer, and Roslyn's `.editorconfig`
  severity mapping applies to analyzer diagnostics — so `dotnet_diagnostic.DM0005.severity = none` had
  no effect. `NoWarn`, `WarningsAsErrors` and `#pragma warning disable` are applied at the compilation
  level and do work; the reference now says that instead.

- **Two traps are written down rather than left to be discovered.**
  `DependencyModules_GenerateFactories` emits a factory per registration, which
  `Microsoft.Extensions.DependencyInjection` cannot see inside — so it silently disables
  `ValidateOnBuild` and `ValidateScopes` for the whole project, measured on the same captive
  dependency with only that property differing. And an assembly declaring two modules that neither set
  `OnlyRealm` puts the whole registration list in both, so loading both in one `AddModules` call
  registers everything twice.

- **`DependencyModules_*` properties are invisible over a `ProjectReference`.** They reach the
  generator through `build/DependencyModules.SourceGenerator.targets`, which ships inside the NuGet
  package, so a project referencing the analyzer as a project never imports it and every property
  silently takes its default — `DependencyModules_LogOutputDirectory` included, producing no log and
  no message. Troubleshooting now says so and gives the `CompilerVisibleProperty` block, and this
  repository's own integration projects declare it.

- **`DecorateAttribute`'s documentation said the opposite of the README.** Its `service` parameter
  was documented as "may be an open generic", which reads as a service *registered* as one. It means
  a generic service named unbound, expanded across the closed constructions the compilation
  registers. The decorators guide also described the failure as an `InvalidOperationException` from
  `DecoratorHelper`; that guard is unreachable from generated code, because the expansion drops the
  decorator before anything is emitted, so the guide now points at `DM0013`.

- **The README leads with the problem rather than the mechanism.** It opened by naming the
  implementation, which says what the thing is before the reader knows why they want one, and the
  most persuasive artefact in it — a generated registration that is plainly ordinary C# — sat at the
  bottom below four hundred lines of reference. It now opens with the hook, a link to the
  documentation site, the attribute beside the code it generates, and a table answering the question
  every reader of a .NET DI library arrives with, which is why not Scrutor. The reference the site
  covers in depth is a lookup table that links out, and the samples in `integ-tests/` are pointed at
  rather than left to be discovered.

- **Three documented examples did not compile, all found by building them.** The README quick start
  omitted `using DependencyModules.Runtime.Attributes;`. The README and the modules guide both showed
  top-level statements naming `ApplicationModule` without importing the root namespace it is generated
  into — `integ-tests/ConsoleTestProject` has always carried that `using`; the docs omitted it and
  sent the reader into a `CS0246` naming a type they never wrote. The testing bootstrap referenced a
  module without importing its namespace, which fails the same way on the generated attribute.

- **`[InjectValues]` was documented as being for the one thing it cannot do.** It was introduced as
  taking "a string, an id, a record combining both", and the comparison table said "the parameter is
  data, not a service". The values are the parameter type's *constructor arguments*, so asking for a
  bare `string` tries to construct `System.String` from a string and fails. The prose beneath it was
  already correct; only the framing promised otherwise. Data rows are what a parameter that simply
  *is* a value wants, and `[InlineData]` composing with `[ModuleTest]` is now shown, since nothing
  said so.

- **Breaking, for anyone building on the generator extension points:**
  `DecoratorExpansion.Expand` takes an additional `out IReadOnlyList<DecoratorModel>` parameter,
  carrying the decorators it refused so the caller can report them. These are the extension points
  the convention generator uses and are not a versioned API — see the
  [generator guide](https://ipjohnson.github.io/DependencyModules/guide/extending).

## [1.0.0-rc9230] - 2026-08-12

Everything since `1.0.0-rc9210`. Still a release candidate: convention registration and the NUnit
integration are both new, and `DecoratorRegistration` changed shape, so the surface is not committed
to yet.

### Added

- **`DependencyModules.NUnit`.** An NUnit integration, with the same `[ModuleTest]` a test author
  already knows: name your modules, take the services you need as method parameters. Everything
  neutral is genuinely shared rather than reimplemented — `[Mock]`, `[InjectValues]`,
  `[TestExport]`, keyed services, the parameter resolution rules, and all three mocking packages
  work against it unchanged.

  **A container per test iteration**, not per test case. Each `[Repeat]` pass and each `[Retry]`
  attempt builds and tears down its own container, and that container's lifetime brackets the whole
  iteration — `[SetUp]`, the test method, then `[TearDown]` — so setup and teardown run while it is
  alive. Wrapping only the method invocation would have left `[SetUp]` running before the container
  existed and `[TearDown]` after it was disposed.

  **Data rows use `[ModuleTestCase]`, not NUnit's `[TestCase]`.** `[TestCase]` requires a row to
  supply an argument for every parameter and enforces that when the case is built, before any of
  this package's code runs, so it cannot express "the row covers the leading parameters and the
  container covers the rest". It also builds its own cases, so combining the two would produce a
  case per row plus one more. `[ModuleTestCase]` is the same idea without that rule; a row may
  supply fewer arguments than the method takes, and `IModuleTestDataAttribute` lets a row come from
  somewhere other than an attribute literal.

  Reference one integration or the other. Both define a `ModuleTestAttribute`, sharing a name and
  nothing else — each derives from what its own framework requires, and only `IModuleTestAttribute`
  is common to the two.

- **Guide pages for each test framework and for mocking.** The testing section now separates what is
  shared from what is not: `Testing modules` carries the framework-neutral core — `[ModuleTest]`,
  assembly-level module attributes, container-per-test, the order a parameter is resolved in,
  `[TestExport]` and `[InjectValues]` — with `xUnit` and `NUnit` pages covering only what differs,
  and a `Mocking frameworks` page covering `[Mock]` and NSubstitute, Moq and FakeItEasy in turn.

  The `DependencyModules.Conventions` install step is gone from the docs along with the package;
  conventions need nothing beyond `DependencyModules.Runtime` and `DependencyModules.SourceGenerator`.
  The conventions guide also no longer claims an implicit `public void Conventions(…)` fails to
  compile — both that and the explicit form are matched now that the contracts are public types.

### Changed

- **Module loading costs about a third less to start.** Registering 200 services takes 6µs; the
  first `AddModules` call took 4.16ms and the second 0.03ms, so nearly all of it was one-time JIT
  and type loading rather than work that scales with the number of services. An empty module cost
  2.9ms against a 0.62ms floor for a bare `ServiceCollection`. Measured against that:

  `ProcessModuleEnvironment` built a `ConcurrentDictionary` on every `AddModules` call to serve a
  cache most applications never read; it is allocated on first process read instead. Module
  discovery used `List.Contains`, which routes through `EqualityComparer<IDependencyModule>.Default`
  — constructing that for an interface was the single most expensive step in the load path, to
  compare a list that usually holds one item. The interface defaults returned
  `ArraySegment<T>.Empty` and reached the empty case by building an enumerator; they return
  `Array.Empty<T>()` and the empty case is a `Count` test. The environment lookup and its guard
  walked the collection twice and now share one scan. The lists in `DependencyRegistry<T>` allocate
  on first use, and the `System.Linq` tokens in `GetModules` moved behind a non-inlined method so
  that assembly is not loaded for applications that never call `AddModule`.

  Empty module 2.92ms → 1.81ms; 200 services 4.44ms → 3.17ms; 42 → 34 methods JIT-ed. Native AOT
  startup was already 0.02ms and is unchanged — there is no JIT there, so for AOT this is a size
  change, 33KB off a published binary.

- **`ApplicationModule` defers to a declared module instead of repeating it.** A project with a
  `Program.cs` gets an `ApplicationModule` whether or not it declares a module of its own, and both
  are modules with no realm restriction, so both registered every service in the compilation — the
  registrations, decorations *and* interceptions were each emitted twice, byte for byte. In a 200
  service project the duplicate was 5,413 bytes of IL, 44% of the assembly and 21% of the
  ReadyToRun image, dead in every application that never names `ApplicationModule`.

  The auto module now returns the declared one from `InternalGetModules`, so
  `AddModule<ApplicationModule>()` registers exactly what it always did from one copy. It defers
  only to a module with no realm restriction and no constructor parameters, since an `OnlyRealm`
  module takes just the registrations aimed at it and deferring to one would drop the rest.
  Assembly IL for that project falls from 17,763 to 12,337 bytes.

  **Behaviour change:** loading `ApplicationModule` alongside the module it defers to now registers
  each service once. It previously registered everything twice, because the two carried independent
  copies of the same registrations.

- **`DecoratorRegistration` is a sealed class rather than a readonly struct.** As a struct it forced
  its own instantiation of `List<T>` and of the LINQ ordering machinery — 44 methods JIT-ed to sort
  three decorators, 13% of every method compiled in the process. Ordering is a stable insertion sort
  now, so no LINQ is instantiated for it at all.

  **Breaking:** this changes the signature encoding of `IEnumerable<DecoratorRegistration>`, so an
  assembly compiled against an earlier runtime throws `MissingMethodException` from
  `IDependencyModule.InternalGetDecorators` until it is rebuilt. Source is unaffected.

- **A generator declaring its own module attribute gets the module written for it.**
  `BaseSourceGenerator.SetupRootGenerator` was `virtual` and empty, so a framework naming its own
  attribute through `ModuleAttributeTypes()` and not overriding it compiled cleanly, emitted no
  module partial, and failed at its consumer's `AddModule<T>()` — a generic constraint error naming
  neither the generator nor the omission. Nothing else can write those modules, so it now writes
  them by default.

  The default is still to write nothing for a generator triggering on `[DependencyModule]`, which is
  adding registrations to modules this package's own generator already writes — the shape the
  extension guide documents. Declaring your own attribute is what tells the two apart. A framework
  wanting its attribute as a marker only, with no module written for it, overrides the method with
  an empty body.

- **`IServiceRegistrationAttribute` documents what it is.** It is the shape of a registration —
  `As`, `Key`, `Lifetime`, `Using` — for reading one uniformly at run time, and it is not how the
  generator finds registration attributes. Nothing tested for it, so an attribute implementing it
  was silently never read, and the interface being public and otherwise unused invited exactly that.
  Registration attributes are matched by type, which is what keeps them on
  `ForAttributeWithMetadataName` and out of a syntax provider that re-runs over every node in the
  compilation per keystroke. The doc comment now says so, and points at the seam that does work.

- **`[TestExport]` moved to `DependencyModules.Testing`.** It registered through
  `ITestServiceSetupAttribute` and had no test framework dependency left, so both integrations now
  get the same attribute rather than a copy. It joins `[Mock]` and `[InjectValues]`, which were
  already there.

  **Breaking, pre-1.0:** a test file needs `using DependencyModules.Testing.Attributes;` alongside
  the one for `[ModuleTest]` — already what a file using `[Mock]` does.

- **Module declaration is no longer welded to one test framework.** `ModuleTestAttribute` now
  implements `IModuleTestAttribute`, a two-line interface in `DependencyModules.Testing` carrying
  the module types, and module loading reads that rather than naming the xUnit attribute. Additive
  for anyone using `[ModuleTest]`.


- **xUnit v3 updated from 1.0.0 to 3.2.2.** `[ModuleTest]` builds on xUnit's extensibility surface —
  a custom test case and discoverer — and that surface moved across the two major versions. Module
  tests now pick up the conditional-skip family the way `[Fact]` and `[Theory]` do: `SkipExceptions`
  on the test case, and per-row `SkipType`/`SkipUnless`/`SkipWhen`/`Label` on a data row, each of
  which previously had nowhere to go.

  `DependencyModules.xUnit` also now references `xunit.v3.extensibility.core` rather than
  `xunit.v3`. It ships `[ModuleTest]` for other people's test projects and is not a test project
  itself, which is exactly what that package is for. It replaces three defensive settings that
  existed only to stop xunit.v3's build targets forcing this library to be an executable.

  **Breaking for anyone constructing `ModuleTestCase` directly:** the constructor gained a
  `skipExceptions` parameter in fifth position, so positional callers past the fourth argument
  need updating. Using `[ModuleTest]` is unaffected.

### Fixed

- **The single-argument apply overloads no longer refuse the environment they were given.**
  `FindOrCreateEnvironment` ran its guard before its lookup, so `DependencyRegistry<T>.ApplyServices`
  and `ApplyDecorators` taking only a service collection — and the generated
  `IDependencyModule.InternalApplyServices(IServiceCollection)` that calls into them — threw for any
  collection holding an `IModuleEnvironment`, including one registered as the singleton instance
  they document as the way to supply it. The message said it was not registered as a singleton
  instance while it was. The lookup runs first now, matching the two-argument path, which always had
  the order right.

  The guard still fires for an environment registered by type or by factory, which cannot decide
  registrations because there is no provider to build it from yet. It now tests the descriptor the
  container would actually resolve rather than any match, so an unusable registration shadowed by a
  usable one is no longer reported.

- **The narrowest `IServiceProviderBuilderAttribute` now wins.** Both integrations took the *first*
  match out of an attribute list ordered widest scope first, so an assembly-level container builder
  silently beat one on the class or the method — the reverse of the interface's own documentation,
  and of how every other test attribute resolves. A method asking for a particular container was
  overridden by a project-wide default with nothing to indicate it. The last match is now taken, so
  method beats class beats assembly, and a test pins the precedence in both frameworks.

  Only one builder is ever used; that part is unchanged. A project declaring exactly one, at any
  single scope, sees no difference.

- **Stacked generators no longer emit the application module twice.** `Program.cs` carries no module
  attribute, so nothing in the syntax said which generator it belonged to and every subclass of
  `BaseSourceGenerator` claimed it. A framework generator loaded alongside this package's own then
  produced a second `ApplicationModule` partial declaring the same members, which does not compile —
  in a console application, the ordinary shape of a consumer. The top level statement module now
  belongs to the generator that owns `[DependencyModule]`; a framework shipping without that
  generator takes it back by overriding `ShouldAutoApproveCompilationUnit`.

- **A module test now reports where it is declared.** `[ModuleTest]` captured no source location and
  the discoverer forwarded none, so a test explorer had nowhere to navigate to and results carried
  no file or line. Both halves are fixed, and a test asserts the location survives the whole way
  onto the discovered test case.

  One limitation, deliberate and pinned by its own test: naming *two or more* modules —
  `[ModuleTest(typeof(A), typeof(B))]` — still reports no location. C# does not allow the
  caller-info parameters that capture it to follow a `params` array, so the multi-module overload
  cannot take them. Naming one module or none captures the location as expected.

### Added

- **.NET 10 support.** Every shipping package now multi-targets `net8.0` and `net10.0`. A .NET 10
  project already worked — a `net8.0` assembly loads fine on it — but the package brought its
  `Microsoft.Extensions.*` 8.x dependency along, and on .NET 10 those live in the shared framework,
  so an older assembly landed in the output in place of the one the framework already supplies. Each
  target framework now carries its own baseline version, so consumers roll forward from it rather
  than being dragged back to it.

  Nothing is dropped: `net8.0` remains a target until it leaves support in November 2026, and the
  generators stay on `netstandard2.0`, which is what Roslyn analyzers must target. The test suites
  and the package verification script run against both frameworks.

- **A test can ask for a `Mock<T>` directly.** With `[MoqSupport]`, a parameter typed
  `Mock<ITemperatureProvider>` hands over the mock itself, so `Mock.Get` is no longer the only way to
  reach it. It works exactly as `[Mock]` does: the service is replaced in the container before
  anything resolves, so the service under test is built against the same mock. No attribute is needed
  — the type says what it is — but `[Mock]` on such a parameter is accepted and simply redundant.

  The two spellings agree. `[Mock] IFoo` and `Mock<IFoo>` on one test give one mock seen two ways,
  and two parameters naming the same `Mock<T>` are one mock. A `[TestExport]` naming a real
  implementation still overrides both, as it already did for `[Mock]`.
- **Environment conditions on decorators.** `[IfEnvironment]` and the rest of the family now take
  effect on a `[Decorator]`, so a decorator can exist only where it is wanted — request logging in
  development, a circuit breaker only in production. Where the condition does not hold the decorator
  is never applied, so the service resolves undecorated rather than being wrapped by something that
  re-tests the environment on every call. A condition changes whether a decorator applies, never
  where it sits in the nesting.
- **Environment conditions on conventions**, as `IfEnvironment(…)`, `IfNotEnvironment(…)`,
  `IfEnvironmentValue(key)`, `IfEnvironmentValue(key, value)` and `IfNotEnvironmentValue(…)`. A whole
  rule can be gated without repeating the attribute on every class it matches. Named after the
  attributes so the two ways of saying the same thing read the same.

  A condition on a convention combines with **and** against any condition on a matched class, so
  neither declaration can silently discard the other. Two conventions matching one class under
  different conditions keep their own guards rather than sharing the stricter one.

### Fixed

- **A condition on a `[Decorator]` was silently ignored.** The attribute compiled, read as
  deliberate, and did nothing: decoration never looked at conditions, so a decorator marked
  `[IfEnvironment("Development")]` wrapped the service in production too.

### Changed

- **The test extensibility hooks moved to `DependencyModules.Testing`** and no longer mention xUnit.
  `ITestParameterValueProvider` and `IServiceProviderBuilderAttribute` moved across as they were but
  now take an `ITestMethodContext` — a `MethodInfo` and the attributes already in scope — in place of
  `IXunitTestMethod`. `ITestStartupAttribute` split along the seam it always had: registering services
  is `ITestServiceSetupAttribute`, running against the built container stays `ITestStartupAttribute`,
  and an attribute that only registers no longer carries a no-op `StartupAsync`.

  This finishes what creating `DependencyModules.Testing` started. Only the pieces that already had
  no xUnit reference moved then, which left a mocking package unable to register anything without
  taking a dependency on a test framework it does not use — which is how `Mock<T>` support is
  implemented without `DependencyModules.Moq` referencing xUnit at all.

  Implementations need a namespace change and the new parameter type; the bodies rarely change, since
  nothing in this repository read more than `.Method` off the xUnit model. An attribute that does need
  the full model can downcast the context to `IXunitTestMethodContext`.
- **Test parameter resolution moved to `DependencyModules.Testing`**, as `TestParameterResolver`.
  Turning a parameter list into arguments is the same problem for any test framework — an attribute on
  the parameter, a keyed service, an ordinary resolution, or constructing an unregistered concrete
  type — and it was buried in xUnit's test case. Behaviour is unchanged, including the order those are
  tried in and the rule that a data row's own arguments cover the leading parameters.

  It is used in two phases either side of the container being built, and resolving without the setup
  phase now throws rather than silently skipping every parameter attribute. Having it addressable on
  its own also means the precedence rules have direct tests instead of being reachable only by running
  a `[ModuleTest]` end to end.
- **`[Mock]` moved from `DependencyModules.xUnit` to `DependencyModules.Testing`**, joining
  `[InjectValues]` in `DependencyModules.Testing.Attributes`. Once the hooks it implements stopped
  naming xUnit, nothing about the attribute was specific to a test framework — so a future
  integration gets the same `[Mock]` rather than a copy of it. Test files need
  `using DependencyModules.Testing.Attributes;` next to the one for `[ModuleTest]`, which is already
  what a file using `[InjectValues]` does.
- **An environment caches what it reads from the process**, misses included, for the life of the
  instance. `IModuleEnvironment` is injectable and the instance `AddModules` registers is held for
  the application's lifetime, so a service reading a value per request was paying a process lookup
  and a fresh string allocation every call — and a miss is the common case, since an optional
  variable that is not set is exactly what a default exists for. Values supplied at the call site are
  unaffected, and the cache is kept separate from them so enumerating an environment still yields
  only what was supplied. The cost is that an instance no longer sees a variable changed
  mid-process.
- **`ModuleEnvironment.Default` is now `ModuleEnvironment.CreateDefault()`**, returning a new
  instance per call rather than one shared by the process. A shared instance that caches would let
  the first read of a variable fix it for every application in the process with no way to opt out —
  the same reasoning that keeps `None` a type of its own. Because each call builds a fresh one,
  asking again is how a current view of the process is obtained.
- **`DecoratorRegistration.RegistryFunc` is now an `EnvironmentRegistryFunc`**, taking the
  environment alongside the collection, so a decorator's condition can be evaluated where it is
  applied. Constructing one is unaffected — the `RegistryFunc` overload remains and adapts — but code
  reading the property and invoking it with a single argument needs the extra parameter. This is
  module plumbing reached through `IDependencyModule.InternalGetDecorators`; hand-written modules
  that decorate directly are unaffected.

## [1.0.0-rc9210] - 2026-08-09

Everything since `1.0.0-rc9200`. Still a release candidate: convention registration is new and
large, and the environment API changed shape late, so the surface is not committed to yet.

### Added

- **Moq and FakeItEasy mocking support**, in new `DependencyModules.Moq` and
  `DependencyModules.FakeItEasy` packages. Apply `[MoqSupport]` or `[FakeItEasySupport]` where you
  would have applied `[NSubstituteSupport]`; `[Mock]` then works the same way. With NSubstitute and
  FakeItEasy the injected instance is also what you configure, while Moq separates the two, so the
  container receives `Mock<T>.Object` and the mock is reached with `Mock.Get(instance)`.
- **`DependencyModules.Testing`**, a test-framework-neutral package holding the pieces the mocking
  packages need — `IMockSupportAttribute`, `IOrderedAttribute`, `IInjectValueAttribute`,
  `InjectValuesAttribute` and `AttributeUtility`. None of them referenced xUnit, but living in
  `DependencyModules.xUnit` meant every mocking package had to depend on a test framework it does not
  use.
- **Convention registration**, in a new `DependencyModules.Conventions` package. A module
  implements `IConventionModule` and declares what to register; the generator resolves the matches
  at compile time and emits ordinary registrations. Selection by assignability, namespace,
  attribute or name glob; shapes `AsSelf`, `AlsoAsSelf`, `AsSelfWithInterfaces`,
  `AsMatchingInterface` and `As<T>`; `Using` and `WithKey` pass through to the registration. The
  declaration body is read rather than executed, so anything that cannot be evaluated at build time
  is reported as DM0009 instead of ignored. Ships as its own analyzer, so a project that does not
  use conventions never loads the class-scanning providers.
- **Scanning a referenced assembly.** `InAssemblyOf<T>()` points a convention at a package instead
  of the project being built. Types are read as symbols during the build and emitted as literal
  `typeof()`, so this survives trimming where a reflection-based scan does not. One named assembly
  at a time; only `public` types are visible.
- **Interception.** `[Intercept]` wraps a service in a generated type that routes every member
  through an interceptor. `IInterceptor`, `IAsyncInterceptor` and `IAsyncEnumerableInterceptor` are
  chosen per member, and a member no interceptor can serve is forwarded untouched. Properties,
  indexers and events are supported; shapes that cannot be wrapped are refused with DM0008.
- **Environment-conditional registration.** `[IfEnvironment]`, `[IfNotEnvironment]`,
  `[IfEnvironmentValue]` and `[IfNotEnvironmentValue]` gate a registration on the environment.
  Conditions of different kinds combine with and. `ModuleEnvironment.Default` reads
  `ASPNETCORE_ENVIRONMENT`, then `DOTNET_ENVIRONMENT`, then `"Production"`. A `ModuleEnvironment` is
  a collection of its values, so they can be written inline —
  `new ModuleEnvironment("Development") { { "REGION", "eu" } }` — and enumerated back out. A key not
  written there falls back to an environment variable of that name; a key written as `null` hides
  one. Lead with `false` — `new ModuleEnvironment(false, "Development")` — to read only what is at
  the call site, which is what a test asserting registrations wants. Conditional registrations are
  emitted after unconditional ones so they can override a default; across modules, module order
  decides.
- **A documentation site** at <https://ipjohnson.github.io/DependencyModules/>, covering conventions,
  decorators, interception, environments, testing, trimming and AOT, and a DM diagnostics reference —
  all of which were previously undocumented.
- DM0004 through DM0012, covering convention ambiguity, a convention matching nothing, an
  unconstructable match, an unreadable declaration, provenance for convention registrations, and
  environment conditions.

### Changed

- **`DependencyModules.xUnit.NSubstitute` is now `DependencyModules.NSubstitute`.** The mocking
  packages do not touch xUnit, and naming one of them after it would have been misleading next to
  `DependencyModules.Moq` and `DependencyModules.FakeItEasy`. Update the `PackageReference` and the
  `using` — the attribute itself is unchanged. Types that moved to `DependencyModules.Testing`
  changed namespace to match, so `DependencyModules.xUnit.Attributes.InjectValuesAttribute` is now
  `DependencyModules.Testing.Attributes.InjectValuesAttribute`, and the extension methods on
  `MethodInfo`/`ParameterInfo` moved from `DependencyModules.xUnit.Impl` to
  `DependencyModules.Testing.Impl`. The xUnit-bound interfaces — `ITestStartupAttribute`,
  `ITestParameterValueProvider` and `IServiceProviderBuilderAttribute`, all of which take an
  `IXunitTestMethod` — stay where they were.
- **`IEnvironmentServiceCollectionConfiguration.ConfigureServices` takes a non-nullable
  `IModuleEnvironment`.** There is now always an environment, so an implementation that branched on
  `null` takes the other branch. Existing implementations still compile.
- **`AddModules` registers the environment it used.** Previously nothing was registered when none
  was supplied, so `GetRequiredService<IModuleEnvironment>()` threw while conditions had been decided
  against the process default. An environment passed to `AddModules` now replaces one already in the
  collection rather than joining it.
- **An `IModuleEnvironment` registered by type or factory is refused.** It cannot be constructed
  while the collection is still being populated, and was previously ignored in favour of the process
  default — so a service gated on `"Development"` quietly took its production branch.
- Generated modules implement both overloads of `InternalApplyServices`. The generator package
  declares no dependency on the runtime package, so a new generator paired with an older runtime
  would otherwise register nothing at all.
- Attribute providers use `ForAttributeWithMetadataName`, roughly halving generator time on a
  2,000-class compilation. This also fixed selection of namespace-qualified attribute usages, which
  were silently not matched.

### Fixed

- **A capability interface could win the default service type.** The service type is inferred from
  the first interface a class declares, so `class ConnectionPool : IDisposable, IPool` registered as
  `IDisposable` and was unresolvable as `IPool` — and a class whose only interface was `IDisposable`
  registered as `IDisposable` rather than as itself. Interfaces describing what a class *can do*
  rather than what it *is* are now passed over: `IDisposable`, `IAsyncDisposable`, `IEquatable<T>`,
  `IComparable`, `ICloneable`, `IConvertible`, `IFormattable`, `IParsable<T>`, `ISerializable`,
  `IEnumerable`/`IEnumerable<T>` and the `INotify*` family. Interfaces that are genuine service roles
  are untouched, including framework ones such as `IEqualityComparer<T>`, `IJsonTypeInfoResolver` and
  `IHttpClientFactory`, as is any type named with `As`. Previously only `INotifyPropertyChanged` was
  skipped.
- **A handler implementing several closings of one interface registered only the first**, silently.
  The MediatR notification shape — one class handling two events — lost every event but one.
- **A `[Decorator]` was matched by conventions as though it were a service.** A decorator implements
  the interface it decorates, so a convention scanning that interface matched the decorator; being
  generic and closing nothing it registered as an open generic, and decoration then refused
  everything with an error blaming the open generic limitation. One open generic decorator over
  convention-registered handlers — the ordinary MediatR shape — could not work.
- **A partial class was two convention candidates**, so a type whose parts each reached the scanned
  interface was reported as ambiguous and registered nothing.
- **A nested type's constructor was used as the outer type's.** Constructor discovery walked the
  whole subtree rather than the type's own members, so a service containing a nested class with a
  parameterised constructor was registered against that constructor. Only visible with
  `DependencyModules_GenerateFactories`.
- **A decorator or interceptor file declared a record module a class**, failing the build with
  CS0261. Two of the four writers contributing to a module's partial carried the record rewrite and
  two did not.
- **`AsSelfWithInterfaces` cross-wired BCL interfaces**, so any type whose base implemented
  `IDisposable` became resolvable as `IDisposable`. Interfaces in `System` and below are no longer
  expanded into.
- **A metadata scan re-registered a package's own services**, ignoring the service attributes that
  exclude a type in the project being built.
- Constructor discovery no longer walks every method body of every candidate, which was the dominant
  cost of the generator on ordinary code: 73 ms to 12 ms on a 2,000-class compilation, measured on
  the run after an edit.
- The README had a code fence opened at *Unit testing* and closed at *Implementation*, so the whole
  *Reporting a problem* section rendered as a C# block on GitHub and on every NuGet package page.

---

## Earlier, in the 1.0.0-rc line

The entries below were written for a 1.0.0 that was not cut. They describe the state reached at
`1.0.0-rc9200` and the decorator work that followed it, and are kept here rather than restated.

### Fixed

- **Generated code no longer requires `ImplicitUsings`.** The generated module attribute was
  emitted as a bare `[AttributeUsage(AttributeTargets...)]`, which only compiled because the
  consuming project happened to have implicit usings enabled. Projects with
  `<ImplicitUsings>disable</ImplicitUsings>` failed with `CS0246`/`CS0103` on every generated
  module. The attribute is now fully qualified.
- **`build/*.props` and `build/*.targets` are packed at the path NuGet honours.** A trailing
  backslash in `PackagePath` produced `build//<PackageId>.props` when packing on Linux or macOS,
  so NuGet did not auto-import them and `ExcludeGeneratedCodeFromCoverage` and
  `DependencyModules_RegistrationType` silently stopped reaching the generator.
- **`DependencyModules_LogOutputDirectory` is now honoured.** Generator diagnostic logs were
  written to the compiler's working directory instead of the configured folder.
- **The generator's configuration properties are reachable from a package.** The packaged
  `build/*.targets` declared only two of the properties the generator reads, so
  `DependencyModules_LogOutputDirectory`, `DependencyModules_RegisterGenerator`,
  `DependencyModules_AutoGenerateModule`, and `DependencyModules_GenerateFactories` silently took
  their defaults for anyone consuming the NuGet package. The diagnostic log in particular could not
  be switched on at all. This went unnoticed because the integration tests reference the generator
  as a project rather than a package, which bypasses `build/*.targets` entirely.
- **A failing generator no longer fails silently.** Exceptions were caught and, with no log
  directory configured, discarded — which also stopped Roslyn reporting its own CS8785. The build
  succeeded, no registrations were produced, and nothing said so. The generator now reports DM0001.
- **`IServiceCollectionConfiguration.ConfigureDecorators` is invoked.** It was declared on the public
  interface and never called by anything, so a module that implemented it silently did nothing.
  It now runs after every module has registered its services, which is what decoration requires.
- **`[CrossWireService(Lifetime = ...)]` is honoured.** The lifetime arrived as the source text
  `"ServiceLifetime.Scoped"`, `Enum.TryParse` failed on the qualified name, and the silent fallback
  registered every cross-wired service as a singleton no matter what was asked for. The existing
  test for this could not detect it: it asserted `Assert.Same(interface1, interface1)`, comparing a
  value to itself.
- **`[ModuleTest]` cases no longer collide across test classes.** The test case unique ID was the
  bare method name, so two test classes each declaring a same-named test produced colliding IDs and
  xUnit silently dropped one — a green run with tests quietly not executing. Discovery now defers to
  xUnit's own `TestIntrospectionHelper`, which derives the ID through the assembly, collection,
  class, and method chain, and also picks up display name formatting, skip and explicit attributes,
  and timeouts consistently with `[Fact]` and `[Theory]`. Test case display names are now
  fully qualified, matching xUnit's convention.
- **Data-driven `[ModuleTest]` rows are named individually.** Every row of an `[InlineData]` module
  test shared one display name, so rows were indistinguishable in test explorers and result files.
  Each row is now named after its own arguments. Parameters injected from the container show as
  `???`, which is xUnit's rendering for a parameter with no discovery-time value.
- **Nested service classes generate valid code.** A service declared inside another type was
  emitted without its containing type — `Namespace.Inner` instead of `Namespace.Outer.Inner` —
  so the generated registration failed to compile with `CS0234`. Note that modules themselves
  still have to be declared directly in a namespace; see the README.
- **Incremental generation now caches.** The generator's model records compared their
  `IReadOnlyList` members by reference, which a positional record does by default. Because every
  module carries at least the `[DependencyModule]` attribute, and the attribute list is rebuilt on
  each run, no two runs ever produced equal models — so Roslyn re-ran full generation on every
  keystroke instead of reusing cached output. `AttributeModel`, `AttributeArgumentValue`,
  `ParameterInfoModel`, `ConstructorInfoModel`, and `ServiceFactoryModel` now compare
  structurally.

### Changed

- `DependencyModules.SourceGenerator` no longer flows `Microsoft.CodeAnalysis.CSharp`,
  `Microsoft.Extensions.Primitives`, or `System.Memory` to consumers. Roslyn supplies these to
  the analyzer at load time; they were never needed in a consumer's dependency graph.
  `DependencyModules.SourceGenerator.Impl` still flows them, because consumers of that
  source-only package compile the shipped sources into their own generator.
- All packages now carry a real description, copyright, and tags instead of NuGet's placeholder
  `Package Description`.
- Packages are built deterministically with [SourceLink](https://github.com/dotnet/sourcelink)
  and ship `.snupkg` symbol packages, so consumers can step into library code.
- Removed the unused `SignAssembly` property and `DependencyModules.snk`. Neither had any
  effect — no key file was wired up, so nothing was ever strong-named. Per
  [Microsoft's library guidance](https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/strong-naming),
  strong naming has no benefit for packages that only ship `net8.0` assemblies.

### Added

- **Decorators.** `[Decorator]` on a class wraps the registered implementation of the service it
  implements and takes as a constructor parameter; `[Decorate(service, decorator)]` on a module does
  the same for types declared elsewhere. `Order` controls nesting, with lower values sitting closer
  to the implementation, and is compared across every module in an `AddModule(s)` call rather than
  only within the declaring one. Open generic decorators are supported, so one decorator can wrap
  every closed registration of a generic service. Two decorators of one service sharing an order is
  reported as DM0007 rather than nesting arbitrarily.

- `tests/DependencyModules.Tests`: unit tests for `DependencyRegistry<T>` (including its
  thread-safety guarantees), module graph loading, generator configuration, and snapshot tests
  over the generator's full output.
- `scripts/verify-packages.sh`: packs the libraries and consumes them from a throwaway project
  the way a real user would, asserting on package layout, metadata, generator execution, and
  MSBuild property flow. Runs in CI.
- `scripts/coverage.sh`: runs every suite with coverage collection, merges the results across the
  shipping libraries, and fails below a threshold. CI publishes the summary to the run summary and
  a coverage badge to the `badges` branch. It also fails the build if xUnit reports a
  skipped test case with a duplicate unique ID, so silently dropped tests can never pass unnoticed.
- Public API approval tests pinning the surface of every package that ships a referenceable
  assembly, so an accidental breaking change after 1.0.0 fails the build instead of shipping.
- Behavioural verification of generated code: tests compile with the real compiler, load the
  emitted assembly, and assert on resolved instances and lifetimes rather than on the shape of the
  generated text.
- Direct tests for `ModuleTestDiscoverer`, written as plain `[Fact]` tests. Every integration test
  runs through `[ModuleTest]`, so a discoverer fault makes that suite quietly smaller rather than
  red; testing it from outside the framework is what makes such faults visible.
- Lifetime semantics tests that cross scope boundaries. Resolving twice from one provider cannot
  tell a singleton from a scoped service, which is why registering singletons as scoped previously
  went unnoticed by the integration suite.
- Diagnostics, so mistakes surface at build time rather than when the container is built:
  DM0001 for a generator failure, DM0002 for a service that cannot be constructed (an abstract or
  static type, which previously produced a registration that threw at `BuildServiceProvider`), and
  DM0003 for a module that is not `partial`.
- A generator log worth reading. It now records the effective configuration, every module and
  service discovered with its lifetime, key, and realm, and anything skipped along with the reason.
  Enable it with `<DependencyModules_LogOutputDirectory>`.
- A tag-driven release workflow publishing to nuget.org and GitHub Packages.

[1.0.0-rc9330]: https://github.com/ipjohnson/DependencyModules/releases/tag/v1.0.0-rc9330
[1.0.0-rc9230]: https://github.com/ipjohnson/DependencyModules/releases/tag/v1.0.0-rc9230
[1.0.0-rc9210]: https://github.com/ipjohnson/DependencyModules/releases/tag/v1.0.0-rc9210
