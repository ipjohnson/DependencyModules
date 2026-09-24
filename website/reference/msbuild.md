# MSBuild properties

Set these properties in the project file, in a `PropertyGroup`. They are applicable to all modules of the project.

```xml
<PropertyGroup>
  <DependencyModules_RegistrationType>Try</DependencyModules_RegistrationType>
  <GeneratedCodeStyle>KandR</GeneratedCodeStyle>
</PropertyGroup>
```

| Property | Values | Default | Function |
| --- | --- | --- | --- |
| `DependencyModules_RegistrationType` | `Add`, `Try`, `TryEnumerable`, `Replace` | `Add` | The registration type for services that do not set `Using`. If a module sets `Using`, the generator uses the module value. |
| `DependencyModules_GenerateFactories` | `true`, `false` | `false` | If the value is `true`, the registrations use generated factories. If a module sets `GenerateFactories`, the generator uses the module value. |
| `DependencyModules_AutoGenerateModule` | `true`, `false` | `true` | If the value is `false`, the generator does not write `ApplicationModule`. |
| `DependencyModules_RegisterGenerator` | `true`, `false` | `false` | If the value is `true`, all modules register the classes that have `[JsonSourceGenerationOptions]`. If a module sets `RegisterJsonSerializers`, the generator uses the module value. |
| `DependencyModules_LogOutputDirectory` | A folder | Empty | The folder for the generator log files. If the value is empty, the generator writes no log. |
| `ExcludeGeneratedCodeFromCoverage` | `true`, `false` | `true` | If the value is `true`, the generated code puts `[ExcludeFromCodeCoverage]` on the module classes. |
| `GeneratedCodeStyle` | `Allman`, `KandR` | `Allman` | The brace style of the generated code. The value `K&R` has the same effect as `KandR`. |

The values of the Boolean properties and of `GeneratedCodeStyle` are not case-sensitive. The values of `DependencyModules_RegistrationType` are case-sensitive. If the generator does not know the value of `DependencyModules_RegistrationType`, it uses `Add`. If the generator does not know the value of `GeneratedCodeStyle`, it uses `Allman`.

If `ExcludeGeneratedCodeFromCoverage` is `true`, the file with the registrations puts `[ExcludeFromCodeCoverage]` on the module class. Thus the coverage tools also ignore the members that you write in the module, for example `ConfigureServices`. The generator writes this file for all modules of a project that has one or more services. The generated module attribute and the class of the `GenerateUseMethod` method do not get `[ExcludeFromCodeCoverage]`. The interceptor wrappers always have `[ExcludeFromCodeCoverage]`, also if the value is `false`.

`GeneratedCodeStyle` does not have the `DependencyModules_` prefix. Other source generators can read the same property.

The generator also reads the `RootNamespace` and `ProjectDir` properties of the .NET SDK. `RootNamespace` sets the namespace of `ApplicationModule`.

## The generator package declares the properties

The compiler gives a property to the generator only if the project declares it as a `CompilerVisibleProperty` item. The `DependencyModules.SourceGenerator` package does this in `build/DependencyModules.SourceGenerator.targets`. If you reference the generator project with a `ProjectReference`, declare the items in your project. For the list, refer to [Troubleshooting](../guide/troubleshooting.md#the-msbuild-properties-have-no-effect).

## The source package

The `DependencyModules.SourceGenerator.Impl` package reads one more property:

| Property | Values | Default | Function |
| --- | --- | --- | --- |
| `PackageDependencyModuleIncludeSource` | `true`, `false` | Empty | If the value is `true`, the project compiles the source code of the package. |

For more information, refer to [Extending](../guide/extending.md#a-source-generator-for-different-framework-attributes).
