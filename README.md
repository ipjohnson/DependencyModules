# ![logo](https://raw.githubusercontent.com/ipjohnson/DependencyModules/main/assets/logo-readme.svg) DependencyModules

[![NuGet](https://img.shields.io/nuget/v/DependencyModules.Runtime.svg)](https://www.nuget.org/packages/DependencyModules.Runtime/)
[![build](https://github.com/ipjohnson/DependencyModules/actions/workflows/build-package.yaml/badge.svg)](https://github.com/ipjohnson/DependencyModules/actions/workflows/build-package.yaml)
[![coverage](https://raw.githubusercontent.com/ipjohnson/DependencyModules/badges/coverage.svg)](https://github.com/ipjohnson/DependencyModules/actions/workflows/build-package.yaml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/ipjohnson/DependencyModules/blob/main/LICENSE.txt)

DependencyModules is a source generator for `Microsoft.Extensions.DependencyInjection`. You put attributes on your classes. When you compile the project, the generator writes the code that registers these classes in an `IServiceCollection`. The generated code does not use reflection to find services at run time.

A module is a partial class that registers the services of a project. A module can use other modules. The test packages build a service provider from your modules for each xUnit or NUnit test.

Documentation: [ipjohnson.github.io/DependencyModules](https://ipjohnson.github.io/DependencyModules/)

## Install

```shell
dotnet add package DependencyModules.Runtime
dotnet add package DependencyModules.SourceGenerator
```

## Register services

Put a service attribute on each class. The attribute sets the lifetime.

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

[DependencyModule]
public partial class ShopModule;
```

The generator registers `PriceCalculator` as `IPriceCalculator`. `ShopModule` registers all services of the project.

## Load the module

```csharp
using DependencyModules.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Shop;

var services = new ServiceCollection();

services.AddModule<ShopModule>();

var provider = services.BuildServiceProvider();

var calculator = provider.GetRequiredService<IPriceCalculator>();
```

## Tests with modules

```csharp
using DependencyModules.xUnit.Attributes;
using Shop;
using Xunit;

namespace Shop.Tests;

public class PriceCalculatorTests
{
    [ModuleTest(typeof(ShopModule))]
    public void Multiplies(IPriceCalculator calculator)
    {
        Assert.Equal(10m, calculator.Total(2.5m, 4));
    }
}
```

The test gets its parameters from a new service provider. After the test, the test package disposes the service provider.

## Features

- [Services](https://ipjohnson.github.io/DependencyModules/guide/services): lifetimes, keyed services, registration types, cross-wired services, and factory methods.
- [Modules](https://ipjohnson.github.io/DependencyModules/guide/modules): module dependencies, realms, module parameters, and the generated `ApplicationModule`.
- [Conventions](https://ipjohnson.github.io/DependencyModules/guide/conventions): registrations for all classes that implement an interface.
- [Decorators](https://ipjohnson.github.io/DependencyModules/guide/decorators) and [interception](https://ipjohnson.github.io/DependencyModules/guide/interception): code around services.
- [Environments](https://ipjohnson.github.io/DependencyModules/guide/environments): registrations that occur only in some environments.
- [Testing](https://ipjohnson.github.io/DependencyModules/guide/testing): xUnit and NUnit tests with modules, mocks, and more service providers in a test.
- [Native AOT](https://ipjohnson.github.io/DependencyModules/guide/aot): generated factories and trimming.

## Sample projects

The `integ-tests` folder contains projects that reference the source projects of the solution. The solution builds them, and the build workflow runs their tests:

- `SutProject` and `SecondarySutProject`: modules and services.
- `SutProject.Tests`: xUnit tests of these modules.
- `SutProject.NUnitTests`: NUnit tests of these modules.
- `ConsoleTestProject`: a console application.
- `web/WebApiApp` and `web/WebApiApp.Tests`: an ASP.NET Core application and its tests.

## Packages

| Package | Contents |
| --- | --- |
| [DependencyModules.Runtime](https://www.nuget.org/packages/DependencyModules.Runtime/) | The attributes, the interfaces, and the `AddModule` methods. |
| [DependencyModules.SourceGenerator](https://www.nuget.org/packages/DependencyModules.SourceGenerator/) | The source generator. It operates only when you compile. |
| [DependencyModules.xUnit](https://www.nuget.org/packages/DependencyModules.xUnit/) | `[ModuleTest]` for xUnit v3, with `xunit.v3` version 3. |
| [DependencyModules.xUnit4](https://www.nuget.org/packages/DependencyModules.xUnit4/) | `[ModuleTest]` for xUnit v3, with `xunit.v3` version 4. |
| [DependencyModules.NUnit](https://www.nuget.org/packages/DependencyModules.NUnit/) | `[ModuleTest]` and `[ModuleTestCase]` for NUnit 4. |
| [DependencyModules.Testing](https://www.nuget.org/packages/DependencyModules.Testing/) | The test attributes and interfaces that the xUnit and NUnit packages use. It also gives the test that runs, and a logger that writes to the output of the test. |
| [DependencyModules.NSubstitute](https://www.nuget.org/packages/DependencyModules.NSubstitute/) | `[Mock]` parameters with NSubstitute. |
| [DependencyModules.Moq](https://www.nuget.org/packages/DependencyModules.Moq/) | `[Mock]` and `Mock<T>` parameters with Moq. |
| [DependencyModules.FakeItEasy](https://www.nuget.org/packages/DependencyModules.FakeItEasy/) | `[Mock]` parameters with FakeItEasy. |
| [DependencyModules.SourceGenerator.Impl](https://www.nuget.org/packages/DependencyModules.SourceGenerator.Impl/) | The source code of the generator. A framework can compile this code into a different source generator. |

The target frameworks of the runtime package, `DependencyModules.Testing`, the test packages, and the mock packages are `net8.0` and `net10.0`. The generator is compatible with Roslyn 4.10 and all subsequent versions.

## License

DependencyModules has the [MIT license](https://github.com/ipjohnson/DependencyModules/blob/main/LICENSE.txt).
