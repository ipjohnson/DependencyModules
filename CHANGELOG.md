# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-08-06

First stable release. The public API is unchanged from the `1.0.0-rc*` line; this release
fixes packaging and generated-code defects and commits to the API surface going forward.

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

[1.0.0]: https://github.com/ipjohnson/DependencyModules/releases/tag/v1.0.0
