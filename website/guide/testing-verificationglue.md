# VerificationGlue.Tests

`DependencyModules.VerificationGlue` adds `[ModuleTest]` and `[ModuleTest<TModule>]` to [VerificationGlue.Tests](https://github.com/ipjohnson/VerificationGlue) test projects. The attributes build the test container from your modules. The parameters of a `[VerifyTest]` method get their values from this container, and a `[Mock]` parameter replaces the matching service.

The package targets `net10.0` only, because VerificationGlue.Tests targets `net10.0` only. It is compatible with VerificationGlue.Tests version 0.3.0-preview.1 and all subsequent versions before 0.4.0.

## Install

```shell
dotnet add package DependencyModules.VerificationGlue
dotnet add package VerificationGlue.Tests --prerelease
```

Reference `VerificationGlue.Tests` in the test project as well. Its build files make the project a test application, and they do not come through another package.

VerificationGlue.Tests builds against the Microsoft.Testing.Platform 3 preview. The preview is on the `test-tools` feed, not on nuget.org. Add the feed to the `NuGet.config` next to the solution:

```xml
<packageSources>
  <add key="test-tools" value="https://pkgs.dev.azure.com/dnceng/public/_packaging/test-tools/nuget/v3/index.json" />
</packageSources>
```

The test project is an executable. Run its tests with `dotnet run --project <test project>`, or with `dotnet test` when `global.json` selects the Microsoft.Testing.Platform runner. The [VerificationGlue.Tests guide](https://github.com/ipjohnson/VerificationGlue/blob/main/docs/guide/getting-started-tests.md) gives more information.

## `[ModuleTest]`

`[ModuleTest]` is in the `DependencyModules.VerificationGlue.Attributes` namespace. Put it on the assembly, a class or a method. VerificationGlue.Tests uses the narrowest one. The container loads the modules of the module attributes on the assembly, the class and the method:

```csharp
using DependencyModules.VerificationGlue.Attributes;
using VerificationGlue.Mocks;
using VerificationGlue.Tests;
using static VerificationGlue.VerifySyntax;

namespace Shop.Tests;

[ModuleTest]
[ShopModule]
public partial class OrderServiceTests
{
    [VerifyTest]
    public void Totals_the_order(OrderService sut)
    {
        Verify(sut.Total(quantity: 2, price: 3) == 6);
    }

    [VerifyTest]
    public void Stamps_the_order_with_the_clock([Mock] IClock clock, OrderService sut)
    {
        clock.Now.Returns(new DateTime(2026, 11, 17));

        Verify(sut.Create(quantity: 1, price: 1).Created == new DateTime(2026, 11, 17));
    }
}
```

`[ModuleTest<TModule>]` loads `TModule` and the module attributes in the scope. Use it for a module in the test project itself. The VerificationGlue.Tests generator cannot see the attribute that the DependencyModules generator writes in the same project, so `[LocalModule]` does not reach the container. `[ModuleTest<LocalModule>]` does.

## Parameters

The container supplies a parameter in this order:

1. The registered service for the parameter type. `[FromKeyedServices(key)]` asks for a keyed service.
2. An instance of an unregistered concrete class, built from the container. `[InjectValues]` gives constructor arguments that the container does not have.

VerificationGlue.Tests supplies data rows, `[Mock]` parameters, `CancellationToken`, `TestContext`, `ITestOutput` and default values itself. The [VerificationGlue.Tests parameter guide](https://github.com/ipjohnson/VerificationGlue/blob/main/docs/guide/test-parameters.md) gives the full order.

Each test container also holds an `ICurrentTestProvider` for its test and the test's `ITestOutput`. A `TestOutputLoggerProvider` made with that `ICurrentTestProvider` writes log entries to the test output.

## Mocks

Use `[Mock]` from `VerificationGlue.Mocks`. VerificationGlue.Tests creates the mock, and the container registers it before it is built, so the services that the container creates receive the mock. A mock replaces a `[TestExport]` of the same service.

`[Mock]` from `DependencyModules.Testing`, and the NSubstitute, Moq and FakeItEasy packages, are for the xUnit and NUnit packages. A parameter with the `DependencyModules.Testing` `[Mock]` fails with a message. When a file imports both `DependencyModules.Testing.Attributes` and `VerificationGlue.Mocks`, add this line:

```csharp
using MockAttribute = VerificationGlue.Mocks.MockAttribute;
```

## Shared containers

`Scope = ContainerScope.Class` builds one container for each test class. `Scope = ContainerScope.Assembly` builds one for the test run:

```csharp
[ModuleTest<ShopModule>(Scope = ContainerScope.Class)]
[TestExport(typeof(IOrderStore), Implementation = typeof(InMemoryOrderStore), Shared = true)]
public partial class OrderHistoryTests
{
    [VerifyTest]
    public void Keeps_the_orders(IOrderStore store, [Shared] IAuditLog log, [Mock] IClock clock)
    {
        // store and log are the same instances in each test of the class.
        // clock is a new mock in each test.
    }
}
```

Each test still gets a container of its own, with its own mocks. The tests share only what they ask to share, which is the rule of `DependencyModules.Testing`:

- A `[Shared]` parameter. The shared container creates the service once, and each test gets that instance.
- A `[TestExport(Shared = true)]` on the scope that owns the shared container: the assembly for `ContainerScope.Assembly`, the class or the assembly for `ContainerScope.Class`.

The shared container creates and disposes the shared instances. A test's mock replaces a shared service of the same type in that test. A shared service that the shared container cannot create fails the test with a message.

Two classes that use `ContainerScope.Assembly` and the same bootstrap share one container. `[ModuleTest]` and each `[ModuleTest<TModule>]` are different bootstraps.

## `DependencyModules.Testing` attributes

| Attribute or interface | Under VerificationGlue.Tests |
| --- | --- |
| `IDependencyModuleProvider`, such as `[ShopModule]` | Loads the module. |
| `[TestExport]`, `ITestServiceSetupAttribute` | Registers services after the modules. |
| `[Shared]`, `[TestExport(Shared = true)]` | Shares instances across the tests of a class or assembly container. |
| `[InjectValues]` | Gives constructor arguments to an unregistered concrete class. |
| `IModuleEnvironmentProvider` | Sets the environment before the modules load, in a test container. |
| `ITestStartupAttribute` | Runs after the test container is built. |
| `IServiceProviderBuilderAttribute` | Builds the provider. The narrowest one is used. |
| `ICurrentTestProvider` | Registered in each test container. |

These features are not mapped:

- `[Mock]` from `DependencyModules.Testing` and `IMockSupportAttribute` mocks. VerificationGlue.Tests has its own `[Mock]`, and only the runner can make a parameter a mock.
- The static `CurrentTest`. VerificationGlue.Tests has no current test outside the test's own objects, so `CurrentTest.Provider` stays unset. Take `ICurrentTestProvider` from the container.
- `ITestContainerSource`. It rebuilds a container many times in one test from the pinned parameters. A VerificationGlue.Tests test has one container, and the class and assembly scopes give the reuse across tests.
- `ITestCaseInfo`. It is part of the xUnit and NUnit packages.
- In a class or assembly container, `IModuleEnvironmentProvider` and `ITestStartupAttribute`. Both take the test method, and a shared container is built for no single test. The test containers under it still use them.

## Reflection

VerificationGlue.Tests finds tests without reflection, and gives a container the test's ID but no `MethodInfo`. An attribute that reads `ITestMethodContext.Method`, such as an `IModuleEnvironmentProvider`, gets the method that this package finds by reflection from the test's ID. The package is not checked for Native AOT.
