# MSBuild properties

Set these in a `PropertyGroup` in the consuming project. They reach the generator through the
package's `build/*.targets`.

| Property | Default | |
|---|---|---|
| `DependencyModules_GenerateFactories` | `false` | emit a `new` expression instead of `typeof(T)`, so the container does not construct by reflection |
| `DependencyModules_RegistrationType` | `Add` | the default registration strategy for the project |
| `DependencyModules_AutoGenerateModule` | `true` | generate `ApplicationModule` for a top-level `Program.cs` |
| `DependencyModules_RegisterGenerator` | `false` | register discovered `JsonSerializerContext` types |
| `DependencyModules_ExcludeGeneratedCodeFromCoverage` | `true` | apply `[ExcludeFromCodeCoverage]` to generated members |
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
