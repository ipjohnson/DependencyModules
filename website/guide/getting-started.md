# Getting started

## The problem

Every .NET application wires its services in one place, and that place grows:

```csharp
// Program.cs, eventually
services.AddScoped<IOrderRepository, OrderRepository>();
services.AddScoped<ICustomerRepository, CustomerRepository>();
services.AddSingleton<IEmailSender, SmtpEmailSender>();
services.AddScoped<IPricingRules, PricingRules>();
// … and another two hundred lines
```

Nothing checks that this list is complete. Write a new class, forget to add its line, and the failure
shows up at run time:

```
System.InvalidOperationException: Unable to resolve service for type
'MyApp.IPricingRules' while attempting to activate 'MyApp.OrderService'.
```

Usually in the environment you deployed to, rather than the one you tested in.

The common escape is a runtime scanner such as Scrutor: describe the types once, and let reflection
find them when the application starts. That does remove the list, but it costs you three things. You
can no longer read what was registered. The scan runs on every start. And the trimmer cannot see
through reflection, so a published, trimmed or Native AOT build registers nothing and fails at
startup — a failure that never reproduces in development.

## How DependencyModules helps

You declare registration next to the class it belongs to, and a source generator writes the
`services.AddScoped(…)` calls into your assembly **while the project builds**.

The hand-written list comes back, except you did not write it and cannot forget a line. Because it is
ordinary C# in your own assembly, there is nothing to reflect over at startup and nothing for the
trimmer to lose.

## Install

```shell
dotnet add package DependencyModules.Runtime
dotnet add package DependencyModules.SourceGenerator
```

Requires .NET 8.0 or later, and ships both `net8.0` and `net10.0` assemblies so a project on either
LTS release gets one built against its own framework. Two more packages are optional, and this guide
will tell you when you want them:

| Package | For |
|---|---|
| `DependencyModules.Conventions` | [registering by rule](/guide/conventions) instead of per class |
| `DependencyModules.xUnit` | [building a provider in tests](/guide/testing) from your real modules |

## Your first module

Two pieces. First, mark the class you want registered:

```csharp
using DependencyModules.Runtime.Attributes;

namespace MyApp;

public interface IEmailSender { void Send(string to); }

[SingletonService]
public class SmtpEmailSender : IEmailSender {
    public void Send(string to) { }
}
```

Second, declare a **module** — a `partial` class the generator fills in. It collects every marked
class in the project:

```csharp
[DependencyModule]
public partial class ApplicationModule;
```

`partial` is required. The generator completes the class you declared; without `partial` there is
nothing to complete, and you get [DM0003](/reference/diagnostics#dm0003).

Now load it at your composition root:

```csharp
using DependencyModules.Runtime;

var services = new ServiceCollection();

services.AddModule<ApplicationModule>();

var provider = services.BuildServiceProvider();
var sender = provider.GetRequiredService<IEmailSender>();   // SmtpEmailSender
```

That is the whole loop: mark the class, declare the module once, load the module once.

::: tip Call AddModule once
Modules pull in other modules through attributes rather than by calling `AddModule` inside each
other — see [Modules](/guide/modules#composing-modules). Calling it once at the composition root
keeps the registration order predictable and avoids registering anything twice.
:::

## Proving to yourself that nothing is hiding

The generated code is the ground truth, and it is worth looking at once so the rest of this guide
reads as concrete rather than magic. Turn it on:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>
```

Build, then open `obj/…/ApplicationModule.Dependencies.g.cs`. Inside it:

```csharp
private static void ModuleDependencies(IServiceCollection services) {
    services.AddSingleton(typeof(MyApp.IEmailSender), typeof(MyApp.SmtpEmailSender));
}
```

One line, and it is the line you would have written by hand. No reflection, no startup scan, and a
literal `typeof()` the trimmer can follow.

::: warning If you redirect the output, clear it between builds
`CompilerGeneratedFilesOutputPath` pointing at a folder inside your project means stale files from a
previous build compile alongside fresh ones, producing a wall of `CS0111`/`CS0579`. Delete the folder
when you rename a module.
:::

## Where to go next

- [Modules](/guide/modules) — grouping registrations and composing them across projects
- [Registering services](/guide/services) — lifetimes, keys, factories, `As`, `Try`/`Replace`
- [Conventions](/guide/conventions) — when attributing each class stops scaling
- [Testing modules](/guide/testing) — building a provider from your real modules in a test
