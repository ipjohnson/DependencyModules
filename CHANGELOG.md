# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

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

[1.0.0-rc9210]: https://github.com/ipjohnson/DependencyModules/releases/tag/v1.0.0-rc9210
