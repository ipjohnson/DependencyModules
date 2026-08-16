# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
