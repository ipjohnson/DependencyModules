# Modules

## The problem

A single project registering everything is fine until it is not. Two things push back:

**Your own application grows areas.** Data access, messaging, and diagnostics each have their own
services, and you would like to reason about them — and switch them out — as units rather than as one
undifferentiated pile of registrations.

**A library cannot register itself.** If you ship a package, its services have to end up in the
consumer's container somehow. The usual answer is to export an `AddMyLibrary(this IServiceCollection)`
extension method and hope everybody remembers to call it, in the right order, once.

## How DependencyModules helps

A **module** is a unit of registration you can name, and modules pull each other in. A library
declares its own module; an application references it and gets everything the library registers
without knowing what any of it is.

## Declaring one

A module is a `partial` class carrying `[DependencyModule]`. The generator completes the partial with
the code that applies its registrations:

```csharp
[DependencyModule]
public partial class ApplicationModule;
```

By default it collects every attributed service in its project. That is the whole declaration — the
body stays empty unless you want something from the rest of this page.

::: warning Two rules
A module **must** be `partial`, or the generator has nothing to complete —
[DM0003](/reference/diagnostics#dm0003).

A module must be declared **directly in a namespace**, never nested inside another type. A nested
module quietly generates a separate, detached class instead of completing your partial, so its
registrations never run. Services can be nested freely; the restriction is only on modules.
:::

## Composing modules

Every module generates **an attribute with the same name**. Applying that attribute to another module
makes it a dependency:

```csharp
[DependencyModule]
public partial class DataModule;

[DependencyModule]
[DataModule]                       // everything DataModule registers comes along
public partial class ApplicationModule;
```

Loading `ApplicationModule` now also applies `DataModule`. This is what replaces the
`AddMyLibrary(services)` extension method: a package ships a module, and consuming it is one
attribute rather than a call somebody has to remember.

```csharp
services.AddModule<ApplicationModule>();   // DataModule comes too
```

Dependencies are expanded **before** the module that declares them, so a module's own registrations
are applied last and win wherever the container is last-wins. An application can therefore override
something a library registered simply by registering it itself.

## Loading modules

```csharp
using DependencyModules.Runtime;

services.AddModule<ApplicationModule>();
services.AddModules(new ApplicationModule(), new DiagnosticsModule());
```

`AddModules` also accepts an [environment](/guide/environments), which is what conditional
registrations get evaluated against:

```csharp
services.AddModules(new ModuleEnvironment("Development"), new ApplicationModule());
```

## You may not need to declare one

For applications using [top-level statements](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/program-structure/top-level-statements),
an `ApplicationModule` is generated for you from `Program.cs`:

```csharp
[assembly: SomeOtherModule]        // compose other modules at the assembly level

var services = new ServiceCollection();

// SomeOtherModule, plus every attributed service in this project
services.AddModule<ApplicationModule>();
```

This is why the ASP.NET sample in this repository never declares a module — the web project's
`Program.cs` gets one automatically, and the test project composes it by name.

## Realms: keeping a registration out of the default module

By default an attributed service joins every module in its compilation. Occasionally that is wrong —
a profiler you only want when the diagnostics module is loaded, say. A **realm** scopes a
registration to one named module:

```csharp
[SingletonService(Realm = typeof(DiagnosticsModule))]
public class Profiler : IProfiler { }
```

`Profiler` is now registered only by `DiagnosticsModule`, and an application that does not compose
that module never sees it.

The reverse restriction is on the module itself. `OnlyRealm = true` means the module takes **nothing**
that did not name it:

```csharp
[DependencyModule(OnlyRealm = true)]
public partial class DiagnosticsModule;
```

Convention registrations always name their declaring module as their realm, which is why two modules
running conventions over the same interface do not leak into each other.

::: warning Two modules in one assembly, loaded together, register everything twice
"Joins every module in its compilation" is literal. An assembly declaring two modules that neither set
`OnlyRealm` puts the *whole* registration list in both — decorators included — so loading both in one
call runs it twice:

```csharp
services.AddModules(new AppModule(), new DataModule());   // every service registered twice
```

Declaring two modules is fine; loading both is what doubles up. If they are meant to be composed
together, give one a realm, or have one compose the other with its
[generated attribute](#composing-modules) instead of naming both at the call site.
:::

## Parameters

A module can take values from whoever loads it — a connection string, a base URL. Declare them as
properties, and the generated attribute mirrors them:

```csharp
[DependencyModule]
public partial class ApplicationModule {
    public string? ConnectionString { get; set; }
}
```

```csharp
[ApplicationModule(ConnectionString = "Server=…")]
public partial class TestModule;
```

## When attributes are not enough

Some registration cannot be expressed as an attribute on a class — `AddHttpClient()`, options
binding, anything from a third-party library with its own extension method. Implement
`IServiceCollectionConfiguration` on the module and you get the collection directly:

```csharp
[DependencyModule]
public partial class ApplicationModule : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        services.AddHttpClient();
    }
}
```

It runs **after** the module's own registrations, with unrestricted access. There is a matching
`ConfigureDecorators` that runs after every module's decorators, and an
`IEnvironmentServiceCollectionConfiguration` that also hands you the
[environment](/guide/environments#programmatic-access).

## Next

- [Registering services](/guide/services) — what each attribute emits
- [Conventions](/guide/conventions) — registering by rule instead of per class
