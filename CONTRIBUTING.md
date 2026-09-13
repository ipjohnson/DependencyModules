# Contributing

## Setup

```sh
git config core.hooksPath .githooks
git config blame.ignoreRevsFile .git-blame-ignore-revs
dotnet tool restore
```

The first line turns on the pre-commit hook, which rejects a commit whose C# is not formatted. Git
does not carry hooks across a clone, so this is the one step that cannot be automated for you.

The second keeps the CSharpier reformat out of `git blame`, which otherwise reports it as the last
change to nearly every line in the repo. GitHub already reads that file without being asked.

## Formatting

C# layout is [CSharpier](https://csharpier.com)'s, and the version is pinned in
`.config/dotnet-tools.json` so every clone and CI agree on what formatted means. Braces are Allman.
Nothing about the style is up for discussion in review — run the formatter:

```sh
dotnet csharpier format .
```

`.editorconfig` describes the same layout for your IDE, so typing and formatting do not disagree.
Project files are excluded (see `.csharpierignore`); CSharpier reindents MSBuild XML but leaves the
interior of multi-line comments where it was, which this repo has a lot of.

`build-package.yaml` runs `dotnet csharpier check .` on every pull request. The hook is the fast
answer, that check is the guarantee.

## Build and test

```sh
dotnet build DependencyModules.sln
dotnet test DependencyModules.sln
```

Both target frameworks are built, so running the tests needs the .NET 8 runtime alongside the .NET
10 SDK that `global.json` selects.

`./scripts/coverage.sh 85` runs every suite with coverage and fails under the threshold, the same
way CI does. `./scripts/verify-packages.sh` packs the libraries and consumes them from a real
package reference, which is the only thing that catches a packaging fault.
