# Getting started

DependencyModules is a source generator for `Microsoft.Extensions.DependencyInjection`. You put attributes on your classes. When you compile the project, the generator writes the code that adds these classes to an `IServiceCollection`.

The generated code does not use reflection to find services at run time. It contains one registration call for each service.

## Before you start

- The project must have the target framework `net8.0` or a subsequent version. The packages contain assemblies for `net8.0` and `net10.0`.
- The C# compiler must contain Roslyn 4.10 or a subsequent version. The .NET SDK 8.0.300 and all subsequent SDKs contain this compiler.

## Install the packages

Add the two packages to the project that contains your services:

```shell
dotnet add package DependencyModules.Runtime
dotnet add package DependencyModules.SourceGenerator
```

`DependencyModules.Runtime` contains the attributes and the types that the generated code uses. It has one dependency: `Microsoft.Extensions.DependencyInjection.Abstractions`. The version of this dependency is 8.0.0 for `net8.0` and 10.0.0 for `net10.0`.

`DependencyModules.SourceGenerator` contains the generator. The generator operates only when you compile. The build output does not contain the generator. The package sets `DevelopmentDependency` to `true`. If you pack your project as a NuGet package, your package does not get a dependency on the generator package.

To build a service provider, the application must also have the `Microsoft.Extensions.DependencyInjection` package. ASP.NET Core applications and applications that use the .NET generic host contain this package.

## Register a service

Put a service attribute on each class that you want in the service collection. Each attribute sets one lifetime:

| Attribute | Lifetime |
| --- | --- |
| `[SingletonService]` | `ServiceLifetime.Singleton` |
| `[ScopedService]` | `ServiceLifetime.Scoped` |
| `[TransientService]` | `ServiceLifetime.Transient` |

```csharp
using DependencyModules.Runtime.Attributes;

namespace Shop;

public interface IPriceCalculator
{
    decimal Total(decimal price, int quantity);
}

[SingletonService]
public class PriceCalculator : IPriceCalculator
{
    public decimal Total(decimal price, int quantity) => price * quantity;
}
```

The generator registers `PriceCalculator` as `IPriceCalculator`, because `PriceCalculator` implements this interface. For more information about the service type, refer to [Services](./services.md).

## Declare a module

A module is a partial class with the `[DependencyModule]` attribute. The generator writes the other part of the class. This part adds the services of the project to a service collection.

```csharp
using DependencyModules.Runtime.Attributes;

namespace Shop;

[DependencyModule]
public partial class ShopModule;
```

The class must be `partial`. If the class is not partial, the generator gives the error DM0003. If the project also has services, the compiler gives the error CS0260.

## Load the module

Call `AddModule<T>()` on the service collection. Then build the service provider.

```csharp
using DependencyModules.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Shop;

var services = new ServiceCollection();

services.AddModule<ShopModule>();

var provider = services.BuildServiceProvider();

var calculator = provider.GetRequiredService<IPriceCalculator>();

Console.WriteLine(calculator.Total(2.50m, 4));
```

In an ASP.NET Core application, call `AddModule<T>()` on `builder.Services`:

```csharp
using DependencyModules.Runtime;
using Shop;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddModule<ShopModule>();

var app = builder.Build();

app.Run();
```

## Generated code

For `ShopModule`, the generator writes two files. The names of the files contain the module name. The `ShopModule.Module.g.cs` file implements `IDependencyModule` on the module. The `ShopModule.Dependencies.g.cs` file contains the registrations:

```csharp
private static void ModuleDependencies(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)
{
    services.AddSingleton(
        typeof(global::Shop.IPriceCalculator),
        typeof(global::Shop.PriceCalculator)
    );
}
```

To see the generated files, set `EmitCompilerGeneratedFiles` to `true` in the project file. The compiler then writes the files below the `obj` folder.

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>
```

## Next steps

- [Services](./services.md) tells you about lifetimes, service types, keys, and factory methods.
- [Modules](./modules.md) tells you how to use modules together and how to select the services that each module registers.
- [Testing](./testing.md) tells you how to write tests that get services from your modules.
