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
- A tag-driven release workflow publishing to nuget.org and GitHub Packages.

[1.0.0]: https://github.com/ipjohnson/DependencyModules/releases/tag/v1.0.0
