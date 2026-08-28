# MSBuild properties

Project-wide settings that change what the generator emits. Set them in a `PropertyGroup` in the
consuming project; they reach the generator through the package's `build/*.targets`, so they work
when the packages are installed from NuGet.

| Property | Default | |
|---|---|---|
| `DependencyModules_GenerateFactories` | `false` | emit a `new` expression instead of `typeof(T)`, so the container does not construct by reflection — [see the trade-off](#generatefactories-and-container-validation) |
| `DependencyModules_RegistrationType` | `Add` | the default registration strategy for the project |
| `DependencyModules_AutoGenerateModule` | `true` | generate `ApplicationModule` for a top-level `Program.cs` |
| `DependencyModules_RegisterGenerator` | `false` | register discovered `JsonSerializerContext` types |
| `ExcludeGeneratedCodeFromCoverage` | `true` | apply `[ExcludeFromCodeCoverage]` to generated members — note this one carries no `DependencyModules_` prefix |
| `GeneratedCodeStyle` | `Allman` | brace style for the generated files: `Allman` or `KAndR`. Unprefixed on purpose — the name is shared with other source generators, so one line styles all of them. An unrecognized value falls back to `Allman` |
| `DependencyModules_LogOutputDirectory` | *(none)* | write a generator log here — see [Troubleshooting](/guide/troubleshooting) |

```xml
<PropertyGroup>
  <DependencyModules_GenerateFactories>true</DependencyModules_GenerateFactories>
  <DependencyModules_LogOutputDirectory>$(MSBuildProjectDirectory)/dmlogs</DependencyModules_LogOutputDirectory>
</PropertyGroup>
```

## Seeing the generated files

Not a DependencyModules property, but the one you will reach for most:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>
```

## `GenerateFactories` and container validation {#generatefactories-and-container-validation}

Worth knowing before turning this on project-wide.

What it emits is a **factory** per registration:

```csharp
services.AddSingleton(
    typeof(OrderService),
    provider => new OrderService(provider.GetRequiredService<IUnitOfWork>()));
```

`Microsoft.Extensions.DependencyInjection` cannot see inside a factory, so every registration in the
project becomes opaque to its own graph validation. Measured on the same captive dependency — a
singleton taking a scoped service — with only this property differing:

| | `BuildServiceProvider(ValidateScopes + ValidateOnBuild)` |
|---|---|
| unset | throws — `Cannot consume scoped service 'IUnitOfWork' from singleton 'OrderService'` |
| `true` | builds cleanly |

A missing registration goes the same way: the `GetRequiredService` call inside a factory is not
checked at build either, so it throws on first resolve instead.

The property exists for startup cost and for Native AOT, which is exactly the setting a team turns on
late and everywhere. If you rely on `ValidateScopes` and `ValidateOnBuild` in development — and the
standard advice is to — keep this off there and turn it on for the published build.

## `GenerateFactories` and per-implementation wrapping

A factory registration cannot say what implementation it built, and that is how
[interception](/guide/interception) finds the one registration to wrap. In 1.1.0 the filter therefore
matched nothing under this property and interception went back to wrapping every registration of the
service type — an unmarked sibling came back inside another class's wrapper, and interceptors ran
once per registration.

From 1.2.0 a service any implementation intercepts keeps its `typeof` registration whatever this
property says. It costs those services the property's benefit and nothing else — the wrapper around
them is still emitted as a literal `new`, and `typeof` is the shape every registration has with the
property off, which is what Native AOT already runs. Nothing to configure.

The same limit reaches `[Decorator(Implementation = …)]`, and there it cannot be worked around the
same way: the decorator is declared on the decorator, so the pass writing the registration it targets
never learns about it. That combination is [DM0022](/reference/diagnostics#dm0022).
