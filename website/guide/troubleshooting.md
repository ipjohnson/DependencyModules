# Troubleshooting

Something is not registered the way you expected. Because every registration is generated code sitting
in your own assembly, you can go and look at it rather than guessing — which makes this a short page.

Three steps produce almost everything needed to diagnose a problem, in the order worth doing them.

## 1. Read the generated code

This answers "was it registered, and as what" definitively, and it is usually the only step you need.

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>
```

The files appear under `obj/`:

| File | Holds |
|---|---|
| `YourModule.Dependencies.g.cs` | the registrations |
| `YourModule.Module.g.cs` | the module plumbing |
| *(separate files)* | decorators and interceptors |

A service missing from `Dependencies.g.cs` was never discovered — jump to the common causes below. A
service present but registered as the wrong service type is a question about `As` and matching, and
[Registering services](/guide/services#what-callers-ask-for) covers it.

::: warning Stale files
If you redirect `CompilerGeneratedFilesOutputPath` into your project, delete the folder between runs.
Stale files compile alongside fresh ones and produce a wall of `CS0111`/`CS0579` that has nothing to
do with your actual problem.
:::

## 2. Turn on the generator log

When the generated file does not explain it, the log says what the generator saw and what it decided
— the configuration in effect, every module and service discovered, and anything skipped **along with
the reason**.

```xml
<PropertyGroup>
  <DependencyModules_LogOutputDirectory>$(MSBuildProjectDirectory)/dmlogs</DependencyModules_LogOutputDirectory>
</PropertyGroup>
```

::: warning No log appeared?
Every `DependencyModules_*` property reaches the generator through
`build/DependencyModules.SourceGenerator.targets`, which ships **inside the NuGet package**. A project
that references the analyzer as a `ProjectReference` — building this library from source, or vendoring
it — never imports that file, so the property is invisible and silently takes its default.

Declare them yourself in that project, or in a `Directory.Build.props` above it:

```xml
<ItemGroup>
  <CompilerVisibleProperty Include="DependencyModules_RegistrationType"/>
  <CompilerVisibleProperty Include="DependencyModules_LogOutputDirectory"/>
  <CompilerVisibleProperty Include="DependencyModules_RegisterGenerator"/>
  <CompilerVisibleProperty Include="DependencyModules_AutoGenerateModule"/>
  <CompilerVisibleProperty Include="DependencyModules_GenerateFactories"/>
  <CompilerVisibleProperty Include="ExcludeGeneratedCodeFromCoverage"/>
</ItemGroup>
```
:::

## 3. Check for DM diagnostics

The generator reports what it can detect at build time, and a good deal of what goes wrong here is
already a warning you have not read yet. See the [diagnostics reference](/reference/diagnostics).

## Common causes

**The module is not `partial`.** [DM0003](/reference/diagnostics#dm0003). The generator completes
your class; without `partial` there is nothing to complete.

**The module is nested inside another type.** A nested module generates a separate, detached class
instead of completing your partial, so its registrations never run. Declare modules directly in a
namespace.

**A convention matched nothing.** [DM0005](/reference/diagnostics#dm0005) — usually a renamed
interface or a typo in a filter.

**A convention picked up something unexpected.** Narrow it with a
[filter](/guide/conventions#narrowing-what-matches). Watch name patterns in particular: `*Handler`
matches `LoggingHandler` too.

**The wrong implementation resolves.** The container takes the **last** matching descriptor for a
single resolve. Check the order in the generated file, remembering that conditional registrations are
emitted after unconditional ones — see [overriding a default](/guide/environments#overriding-a-default).

**A test resolves the wrong environment branch.** Supply nothing and the *process* environment is
used, defaulting to `"Production"`. See
[testing conditional registrations](/guide/testing-registrations#testing-conditional-registrations).

**An assertion on the concrete type fails.** An [intercepted](/guide/interception) or
[decorated](/guide/decorators) service resolves as the wrapper, not your class.

**`AddModule` called more than once.** Calling it inside a module, or several times at the root,
duplicates registrations.

**Two modules declared in one project, both loaded.** Each holds that project's whole registration
list, so loading both applies it twice — and composing one into the other does not help, since both
still hold everything. Give one a [realm](/guide/modules#realms-keeping-a-registration-out-of-the-default-module).

## Reporting a problem

Please include **the generator log and the generated file** in any
[issue](https://github.com/ipjohnson/DependencyModules/issues). Between them they show whether a
service was discovered, which realm it landed in, and what configuration was in effect — which is
most of the way to a diagnosis before anyone has to reproduce it.
