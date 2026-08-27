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

A module must be declared **directly in a namespace**, never nested inside another type —
[DM0017](/reference/diagnostics#dm0017). A nested module quietly generates a separate, detached class
instead of completing your partial, so its registrations never run. Services can be nested freely;
the restriction is only on modules.
:::

## Composing modules

Every module generates **an attribute with the same name**. Applying that attribute to another module
makes it a dependency:

```csharp
// MyApp.Data — its own project
[DependencyModule]
public partial class DataModule;
```

```csharp
// MyApp — references MyApp.Data
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

::: warning The two modules are in two projects, and that matters
A module collects every attributed service **in its own project** — so two modules declared in one
project each hold that project's whole registration list, and composing one into the other does not
change what either holds. Loading `ApplicationModule` would then apply the same registrations twice.

```csharp
// one project, both modules — every service registers twice
[DependencyModule] public partial class DataModule;
[DependencyModule] [DataModule] public partial class ApplicationModule;
```

Composing across projects is the shape above and is what this is for. Two modules that genuinely
belong in one project want [realms](#realms-keeping-a-registration-out-of-the-default-module)
instead: a realm is how you say which registrations belong to which module.
:::

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
using MyApp;                       // the generated module takes your RootNamespace
using DependencyModules.Runtime;

[assembly: SomeOtherModule]        // compose other modules at the assembly level

var services = new ServiceCollection();

// SomeOtherModule, plus every attributed service in this project
services.AddModule<ApplicationModule>();
```

::: warning The first `using` is not optional
The generated module takes the project's `RootNamespace`, and top-level statements sit in the
global namespace — so `Program.cs` cannot see `ApplicationModule` until it imports that namespace.
Leave it out and the build fails with `CS0246: The type or namespace name 'ApplicationModule' could
not be found`, which does not hint at the cause.
:::

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

Declaring two modules is fine; loading both is what doubles up. **Give one a realm** — that is what
says which registrations belong to which module, and the only thing that removes the doubling.

Composing one into the other does *not*, however it reads: both still hold the whole list, so
loading the outer one applies it twice. Composition is for modules in [separate
projects](#composing-modules), where each holds only its own.
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

::: warning A module with parameters needs an identity
Modules de-duplicate **by type**, which is what stops a module reached twice from registering
everything twice. A module carrying parameters is the case that rule does not fit: two instances
holding different values are the same module by it, so the first one reached wins and the other is
discarded with nothing said.

```csharp
[DependencyModule] [ApplicationModule(ConnectionString = "primary")]  public partial class A;
[DependencyModule] [ApplicationModule(ConnectionString = "reporting")] public partial class B;
```

Load both and one connection string arrives. [DM0018](/reference/diagnostics#dm0018) reports it, and
declaring your own `Equals` and `GetHashCode` says which answer you meant — identity by value, so
both survive, or identity by type, so one wins deliberately.
:::

A **value-typed** parameter cannot carry a default. `public int Retries { get; set; } = 3;` is reset
to `0` by a composition that does not name it, because `0` and "not set" are the same value and the
generated attribute cannot tell them apart. A nullable or reference-typed parameter keeps its
default. Name value-typed parameters at every composition, or make them nullable.

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
