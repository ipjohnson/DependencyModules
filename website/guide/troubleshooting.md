# Troubleshooting

## See the generated code

Set `EmitCompilerGeneratedFiles` to `true` in the project file. The compiler then writes the generated files below the `obj` folder.

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>
```

Most IDEs also show the generated files below the analyzers of the project.

The generator writes these files for each module. The name of each file starts with the namespace of the module and the module name. The generator removes the root namespace of the project from the start of the name. If the module is not in the root namespace, the name starts with `global-` and the full namespace, for example `global-Sub.FooModule.Module.g.cs`.

| File | Contents |
| --- | --- |
| `<Module>.Module.g.cs` | The other part of the module and the module attribute. |
| `<Module>.Dependencies.g.cs` | The registrations from service attributes. |
| `<Module>.ConventionDependencies.g.cs` | The registrations from conventions. |
| `<Module>.Decorators.g.cs` | The decorators. |
| `<Module>.Interceptors.g.cs` | The registrations of the interceptor wrappers. |
| `<Class>_Intercepted.g.cs` | The wrapper of one intercepted class. |

## Write a generator log

Set `DependencyModules_LogOutputDirectory` to a folder. The generator then writes log files into this folder.

```xml
<PropertyGroup>
  <DependencyModules_LogOutputDirectory>$(MSBuildProjectDirectory)/generator-logs</DependencyModules_LogOutputDirectory>
</PropertyGroup>
```

Each part of the generator writes a log file each time that the generator runs. The name of each file is the name of the part, the time in milliseconds, eight random characters, and the extension `.txt`. Thus no log file replaces a different log file. The service part, the interceptor part, and the convention part write two files. The name of the second file has the suffix `.Diagnostics` after the name of the part. A log file can be empty.

The log of `ServiceSourceGenerator` shows the configuration, the modules, and the services that the generator found. The logs also show the errors and the exceptions. If the folder has no new log files, build the project again with `dotnet build --no-incremental`. The usual build does not run the generator when no file changed.

## A service has no registration

Examine these causes:

1. The module is not partial. The generator gives the error DM0003.
2. The module is in a different class. The generator gives the error DM0017.
3. The class is abstract or static. The generator gives the warning DM0002.
4. The service is a static factory method that the generated code cannot call. The generator gives the warning DM0023.
5. The attributes must be from `DependencyModules.Runtime.Attributes`.
6. The service sets `Realm` to a different module.
7. The module has `OnlyRealm = true`, and the service does not set `Realm` to this module.
8. The service has environment conditions that are false. For each class with a service attribute and conditions, the generator gives DM0011, which shows the conditions. DM0011 has the severity Info. Thus the build output does not show it at the usual verbosity.
9. The service type of the registration is not the type that you use to get the service. For more information, refer to [Service type](./services.md#service-type).
10. The application must call `AddModule` or `AddModules`, or load a module that has the module attribute.
11. The service is in a different project. The application must load the module of that project. For more information, refer to [Module dependencies](./modules.md#module-dependencies).

If the generator does not write the other part of a module, the module does not implement `IDependencyModule`. Then `AddModule<T>()` gives the error CS0311.

## The service provider gives an incorrect implementation

When a service type has more than one registration, `GetService` gives the instance from the last registration. Examine these sequences:

- The sequence of the registrations in one module. Refer to [Registration sequence](./services.md#registration-sequence).
- The sequence of the modules. Refer to [Module load sequence](./modules.md#module-load-sequence).

To find all registrations, call `GetServices<T>()`. The result contains one instance for each registration.

For a decorated service, the service provider gives the outer decorator. For an intercepted service, it gives the wrapper class. Thus a check for the type of the implementation class fails.

## A service has two registrations

Examine these causes:

- Two modules of the same project register all services that do not set `Realm`. If you load two of these modules, each service registers two times. Realms can divide the services.
- You call `AddModule` two times with the same module. In one `AddModules` call, each module loads one time.

## The MSBuild properties have no effect

The `DependencyModules.SourceGenerator` package declares the MSBuild properties as `CompilerVisibleProperty` items. If you reference the generator project with a `ProjectReference`, and not the package, declare these items in your project:

```xml
<ItemGroup>
  <CompilerVisibleProperty Include="DependencyModules_RegistrationType" />
  <CompilerVisibleProperty Include="DependencyModules_LogOutputDirectory" />
  <CompilerVisibleProperty Include="DependencyModules_RegisterGenerator" />
  <CompilerVisibleProperty Include="DependencyModules_AutoGenerateModule" />
  <CompilerVisibleProperty Include="DependencyModules_GenerateFactories" />
  <CompilerVisibleProperty Include="ExcludeGeneratedCodeFromCoverage" />
  <CompilerVisibleProperty Include="GeneratedCodeStyle" />
</ItemGroup>
```

## The generator failed

If an exception occurs in the generator, the generator gives the error DM0001. Some registrations of the project can then be missing. If the exception occurs for one module, the generator writes the code of the other modules.

Do these steps:

1. Set `DependencyModules_LogOutputDirectory` to a folder.
2. Build the project again.
3. Write an issue for the problem on the [issues page](https://github.com/ipjohnson/DependencyModules/issues) of the repository.
4. Attach the log files to the issue.

## Diagnostics

For each diagnostic, refer to [Diagnostics](../reference/diagnostics.md).
