; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DM0023 | DependencyModules | Warning | A factory method with a service attribute cannot be called by the generated module.
DM0024 | DependencyModules | Warning | A cross-wired class declares no interface but inherits one.
DM0025 | DependencyModules | Warning | A [Decorator] class is not applied.
DM0026 | DependencyModules | Disabled | A service attribute does not name its service type.
DM0027 | DependencyModules | Warning | A collection item cannot be built.
DM0028 | DependencyModules | Error | Two map items have the same key.
DM0029 | DependencyModules | Error | List items use more than one order scheme.
DM0030 | DependencyModules | Warning | A Func parameter gets no registration.

### Removed Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DM0022 | DependencyModules | Warning | A decorator names an implementation while factories are generated.
