# Getting started

DependencyModules turns attributes and conventions into `IServiceCollection` registration code
during the build. There is no container of its own — what comes out is `services.AddScoped(…)` calls
in a file you can read.

## Install

```shell
dotnet add package DependencyModules.Runtime
dotnet add package DependencyModules.SourceGenerator
```

Requires .NET 8.0 or later. Two more packages are optional:

| Package | For |
|---|---|
| `DependencyModules.Conventions` | [registering by convention](/guide/conventions) rather than per class |
| `DependencyModules.xUnit` | [building a provider in tests](/guide/testing) from the modules a test names |

## A first module

A module is a `partial` class. The generator completes it.

```csharp
using DependencyModules.Runtime.Attributes;

namespace MyApp;

public interface IEmailSender { void Send(string to); }

[SingletonService]
public class SmtpEmailSender : IEmailSender {
    public void Send(string to) { }
}

[DependencyModule]
public partial class ApplicationModule;
```

Then load it:

```csharp
using DependencyModules.Runtime;

var services = new ServiceCollection();

services.AddModule<ApplicationModule>();

var provider = services.BuildServiceProvider();
var sender = provider.GetRequiredService<IEmailSender>();
```

::: tip Call AddModule once
Modules compose through attributes rather than by calling `AddModule` inside each other. Calling it
once at the composition root keeps the registration order predictable.
:::

## See what was generated

The generated code is the ground truth, and it is worth looking at once.

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>
```

The files appear under `obj/`, and `ApplicationModule.Dependencies.g.cs` will contain something like:

```csharp
private static void ModuleDependencies(IServiceCollection services) {
    services.AddSingleton(typeof(MyApp.IEmailSender), typeof(MyApp.SmtpEmailSender));
}
```

That is all there is. No reflection, no startup scan, and a literal `typeof()` the trimmer can
follow.

::: warning Delete generated/ between runs
If you point `CompilerGeneratedFilesOutputPath` at a folder inside your project, stale files from a
previous build compile alongside fresh ones and produce a wall of `CS0111`/`CS0579`. Clear it when
you change module names.
:::

## Where to go next

- [Modules](/guide/modules) — composition, realms, parameters and features
- [Registering services](/guide/services) — lifetimes, keys, factories, `As`, `Try`/`Replace`
- [Conventions](/guide/conventions) — declare a rule instead of attributing each class
- [Trimming and AOT](/guide/aot) — why this survives what reflection-based scanners do not
