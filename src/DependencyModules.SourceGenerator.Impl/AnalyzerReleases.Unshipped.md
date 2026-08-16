; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DM0001 | DependencyModules | Error | The generator failed; registrations may be missing.
DM0002 | DependencyModules | Warning | A service type cannot be constructed and was not registered.
DM0003 | DependencyModules | Error | A module marked with [DependencyModule] is not partial.
DM0004 | DependencyModules | Error | Two conventions in one module register a type as the same service type.
DM0005 | DependencyModules | Warning | A convention matched no types.
DM0006 | DependencyModules | Warning | A convention matched a type with no accessible constructor.
DM0007 | DependencyModules | Error | Two decorators of one service share an order.
DM0008 | DependencyModules | Warning | A service marked for interception cannot be wrapped.
DM0009 | DependencyModules | Error | A convention declaration could not be read.
DM0010 | DependencyModules | Info | A service is registered by convention.
DM0011 | DependencyModules | Info | A service is registered only when an environment condition holds.
DM0012 | DependencyModules | Warning | An environment condition names nothing to test.
DM0013 | DependencyModules | Warning | A service registered as an open generic cannot be decorated.
DM0014 | DependencyModules | Warning | A generic type cannot be cross-wired.
DM0015 | DependencyModules | Warning | An interceptor does not apply to every member it was applied to.
DM0016 | DependencyModules | Warning | An assembly-level module attribute's namespace is not imported.
