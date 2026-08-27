; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DM0020 | DependencyModules | Warning | An interception is applied by no module, so it never runs.
DM0021 | DependencyModules | Warning | [Mock] and [TestExport] name one service on the same test method.
DM0022 | DependencyModules | Warning | A decorator names an implementation while factories are generated.
