; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DM0001 | DependencyModules | Error | The generator failed; registrations may be missing.
DM0002 | DependencyModules | Warning | A service type cannot be constructed and was not registered.
DM0003 | DependencyModules | Error | A module marked with [DependencyModule] is not partial.
