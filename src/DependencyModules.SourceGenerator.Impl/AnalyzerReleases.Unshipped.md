; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DM0023 | DependencyModules | Warning | A factory method with a service attribute cannot be called by the generated module.
DM0024 | DependencyModules | Warning | A cross-wired class declares no interface but inherits one.

### Removed Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DM0022 | DependencyModules | Warning | A decorator names an implementation while factories are generated.
