# Troubleshooting

## See the generated code

Set `EmitCompilerGeneratedFiles` to `true` in the project file. The compiler then writes the generated files below the `obj` folder.

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>
```

Most IDEs also show the generated files below the analyzers of the project.

The generator writes these files for each module. The name of each file starts with the namespace of the module and the module name. The generator removes the root namespace of the project from the start of the name.

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

Three parts of the generator write log files: the service part, the interceptor part, and the convention part. The name of each file is the name of the part, the time in milliseconds, and the extension `.txt`. Each part writes two files each time that the generator runs. If the two files get the same name, the last file replaces the first file. Thus a log file can be empty.

The log of `ServiceSourceGenerator` shows the configuration, the modules, and the services that the generator found. The logs also show the errors. If a log is empty, build the project again with `dotnet build --no-incremental`. The usual build does not run the generator when no file changed.

## A service has no registration

Examine these causes:

1. The module is not partial. The generator gives the error DM0003.
2. The module is in a different class. The generator gives the error DM0017.
3. The class is abstract or static. The generator gives the warning DM0002.
4. The attributes must be from `DependencyModules.Runtime.Attributes`.
5. The service sets `Realm` to a different module.
6. The module has `OnlyRealm = true`, and the service does not set `Realm` to this module.
7. The service has environment conditions that are false. For each class with a service attribute and conditions, the generator gives DM0011, which shows the conditions. DM0011 has the severity Info. Thus the build output does not show it at the usual verbosity.
8. The service type of the registration is not the type that you use to get the service. For more information, refer to [Service type](./services.md#service-type).
9. The application must call `AddModule` or `AddModules`, or load a module that has the module attribute.
10. The service is in a different project. The application must load the module of that project. For more information, refer to [Module dependencies](./modules.md#module-dependencies).

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

If an exception occurs when the service part, the interceptor part, or the convention part writes code, the generator gives the error DM0001. Some registrations of the project can then be missing.

If an exception occurs in a different step of the generator, the compiler gives the warning CS8785. The generator then writes no code, and `AddModule<T>()` gives the error CS0311.

In the two conditions, do these steps:

1. Set `DependencyModules_LogOutputDirectory` to a folder.
2. Build the project again.
3. Write an issue for the problem on the [issues page](https://github.com/ipjohnson/DependencyModules/issues) of the repository.
4. Attach the log files to the issue.

## Diagnostics

For each diagnostic, refer to [Diagnostics](../reference/diagnostics.md).
