# Troubleshooting

If services are not registered the way you expect, three steps produce almost everything needed to
diagnose it.

## 1. Read the generated code

The registrations the generator produced are the ground truth.

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>
```

The files appear under `obj/`. `YourModule.Dependencies.g.cs` holds the registrations,
`YourModule.Module.g.cs` the module plumbing, and there are separate files for decorators and
interceptors.

::: warning Stale files
If you redirect `CompilerGeneratedFilesOutputPath` into your project, delete the folder between runs.
Stale files compile alongside fresh ones and produce a wall of `CS0111`/`CS0579`.
:::

## 2. Turn on the generator log

It records the configuration in effect, every module and service discovered, and anything skipped
along with the reason.

```xml
<PropertyGroup>
  <DependencyModules_LogOutputDirectory>$(MSBuildProjectDirectory)/dmlogs</DependencyModules_LogOutputDirectory>
</PropertyGroup>
```

## 3. Check for DM diagnostics

The generator reports what it can detect at build time. See the
[diagnostics reference](/reference/diagnostics) for what each one means and what to do about it.

## Common causes

**The module is not `partial`.** [DM0003](/reference/diagnostics#dm0003). The generator cannot
complete a class it cannot extend.

**The module is nested inside another type.** A nested module generates a separate, detached class
rather than completing the partial, so its registrations never run. Declare it directly in a
namespace.

**A convention matched nothing.** [DM0005](/reference/diagnostics#dm0005) — usually a renamed
interface or a typo in a filter.

**A service was registered but resolves to the wrong implementation.** The container takes the last
matching descriptor for a single resolve. Check the order in the generated file; conditional
registrations are emitted after unconditional ones deliberately.

**A convention picked up something unexpected.** Narrow it with a
[filter](/guide/conventions#narrowing-what-matches), and remember that a name glob is the weakest
selector — `*Handler` matches a decorator named `LoggingHandler` just as readily.

**`AddModule` called more than once.** Modules compose through attributes; calling `AddModule` inside
a module or several times at the root duplicates registrations.

## Reporting a problem

Please include the generator log and the generated file in any
[issue](https://github.com/ipjohnson/DependencyModules/issues). Between them they answer whether a
service was discovered at all, which realm it landed in, and what configuration was in effect —
none of which is visible from the generated output alone.
