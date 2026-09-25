# Contributing

## Before you start

Install these tools:

- The .NET SDK 10.0.302 or a subsequent 10.0 SDK. The `global.json` file selects the SDK.
- The .NET 8 runtime. The tests also run on `net8.0`.
- Node.js 22 and npm, if you change the documentation site.

## Build the solution and run the tests

```shell
dotnet restore DependencyModules.sln
dotnet build DependencyModules.sln --configuration Release
dotnet test DependencyModules.sln --configuration Release
```

To run all tests with code coverage, use the coverage script. The script writes the report to `artifacts/coverage`. If you give a percentage, the script fails when the line coverage is less than that value.

```shell
./scripts/coverage.sh 85
```

The xUnit test projects use `xunit.v3` version 3 and `DependencyModules.xUnit`. To run them with `xunit.v3` version 4 and `DependencyModules.xUnit4`, use the xUnit script:

```shell
./scripts/test-xunit.sh 4
```

The `XunitMajor` property in `Directory.Build.props` selects the version. The script sets it to the major version that you give.

To do a test of the packages that a user gets, use the package script. The script packs the ten packages. Then it builds and runs a test application for each target framework. The test application references these packages.

```shell
./scripts/verify-packages.sh
```

## Code format

CSharpier formats the C# code. The tool manifest in `.config/dotnet-tools.json` sets the version.

```shell
dotnet tool restore
dotnet csharpier format .
dotnet csharpier check .
```

The `.editorconfig` file gives IDEs the same C# format as CSharpier. This format puts braces on new lines (Allman style).

To make sure that the format is correct before each commit, enable the hook in `.githooks`:

```shell
git config core.hooksPath .githooks
```

The hook examines each C# file with staged changes. It examines the files in your folder, not the staged copies. If `dotnet` is not on the `PATH`, the hook does no check.

CSharpier does not format the project files (`.csproj`, `.props`, and `.targets`). When you change the project files, keep their format.

The `.git-blame-ignore-revs` file contains the commit that changed the format of all C# files with CSharpier. GitHub uses this file. To use this file with `git blame`, run this command:

```shell
git config blame.ignoreRevsFile .git-blame-ignore-revs
```

## Pull requests

The `build-package` workflow runs for each pull request to `main`. It does these checks:

1. It does a check of the format with CSharpier.
2. It builds the solution.
3. It runs all tests with code coverage. The line coverage must be 85 percent or more.
4. It runs the xUnit tests again with `xunit.v3` version 4.
5. It runs `scripts/verify-packages.sh`.

The `xunit-prerelease` workflow runs each week. It runs the xUnit tests with the newest `xunit.v3` on nuget.org, prereleases included. A failure tells you about a change in xUnit before its release.

## Documentation

The documentation site is in the `website` folder. It uses VitePress.

```shell
cd website
npm ci
npm run dev
npm run build
```

`npm run build` fails when an internal link has no target page. When you merge a change to `website` into `main`, the `docs` workflow publishes the site to GitHub Pages.

`README.md` is also the NuGet page of each package. Thus the links and images in `README.md` must be absolute URLs.

Write the documentation in [ASD-STE100 Simplified Technical English](https://www.asd-ste100.org/). Use the names from the code for types, members, and attributes.

## Releases

Each push to `main` publishes prerelease packages to GitHub Packages. Their version has the suffix `ci.` and the run number.

To publish a release, push a version tag:

```shell
git tag v1.5.0
git push origin v1.5.0
```

The `release` workflow then does these steps:

1. It builds the code.
2. It runs the tests with `xunit.v3` version 3 and version 4.
3. It packs the ten packages.
4. It publishes the packages to nuget.org and to GitHub Packages.
5. It makes a GitHub release with generated release notes.

A version with a hyphen, for example `1.6.0-preview.1`, is a prerelease.

The tag sets the package version. `Directory.Build.props` sets the version for local builds and for the prerelease packages. The assembly version stays `1.0.0.0` for all 1.x versions.
