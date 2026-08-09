# Modules

A module is a `partial` class carrying `[DependencyModule]`. The generator completes the partial
with the plumbing that applies its registrations.

```csharp
[DependencyModule]
public partial class ApplicationModule;
```

::: warning Two rules
A module **must** be `partial` — otherwise the generator cannot complete it, and reports
[DM0003](/reference/diagnostics#dm0003).

A module must be declared **directly in a namespace**, not nested inside another type. A nested
module generates a separate, detached class rather than completing the partial, so its registrations
never run. Services may be nested freely; only the module itself is restricted.
:::

## Composing modules

Every module generates an attribute of the same name. Applying it to another module makes it a
dependency.

```csharp
[DependencyModule]
public partial class DataModule;

[DependencyModule]
[DataModule]                       // DataModule's registrations come along
public partial class ApplicationModule;
```

Dependencies are expanded before the module that declares them, so a module's own registrations are
applied last and win where the container is last-wins.

## Loading modules

```csharp
using DependencyModules.Runtime;

services.AddModule<ApplicationModule>();
services.AddModules(new ApplicationModule(), new DiagnosticsModule());
```

`AddModules` also takes an [environment](/guide/environments), which is what conditional
registrations are evaluated against:

```csharp
services.AddModules(new ModuleEnvironment("Development"), new ApplicationModule());
```

## Auto-generated application module

For [top-level statement](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/program-structure/top-level-statements)
applications, an `ApplicationModule` is generated for a file named `Program.cs`, so you do not need
to declare one.

```csharp
[assembly: SomeOtherModule]

var services = new ServiceCollection();

// SomeOtherModule, plus every registration in this project
services.AddModule<ApplicationModule>();
```

## Realms

A realm scopes a registration to one module rather than to every module in the compilation.

```csharp
[SingletonService(Realm = typeof(DiagnosticsModule))]
public class Profiler : IProfiler { }
```

A module declared with `OnlyRealm = true` takes **nothing** that did not name it:

```csharp
[DependencyModule(OnlyRealm = true)]
public partial class DiagnosticsModule;
```

Convention registrations always name their declaring module as their realm, so two modules scanning
the same interface do not leak into each other.

## Parameters

A module can take constructor parameters and expose them as properties, which its generated
attribute mirrors.

```csharp
[DependencyModule]
public partial class ApplicationModule {
    public string? ConnectionString { get; set; }
}

[ApplicationModule(ConnectionString = "…")]
public partial class TestModule;
```

## Programmatic registration

For anything the attributes and conventions cannot express, implement
`IServiceCollectionConfiguration`. It runs after the module's own registrations, with unrestricted
access to the collection.

```csharp
[DependencyModule]
public partial class ApplicationModule : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        services.AddHttpClient();
    }
}
```

There is a matching `ConfigureDecorators` that runs after every module's decorators, and an
`IEnvironmentServiceCollectionConfiguration` that also receives the
[environment](/guide/environments).
