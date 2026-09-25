# Testing

The test packages build a service provider from your modules for each test. The test method gets services as parameters. Thus a test uses the same registrations as the application.

## Packages

| Package | Contents |
| --- | --- |
| `DependencyModules.xUnit` | `[ModuleTest]` for xUnit v3, with `xunit.v3` version 3. |
| `DependencyModules.xUnit4` | `[ModuleTest]` for xUnit v3, with `xunit.v3` version 4. |
| `DependencyModules.NUnit` | `[ModuleTest]` and `[ModuleTestCase]` for NUnit 4. |
| `DependencyModules.Testing` | The attributes and interfaces that the test packages use. The test packages reference this package. |
| `DependencyModules.NSubstitute` | `[NSubstituteSupport]` for mocks. |
| `DependencyModules.Moq` | `[MoqSupport]` for mocks. |
| `DependencyModules.FakeItEasy` | `[FakeItEasySupport]` for mocks. |

Add one test package to the test project. Also add a reference to the project that contains your modules. If the test project declares modules or services, also add `DependencyModules.SourceGenerator`.

For more information about each framework, refer to [xUnit](./testing-xunit.md) and [NUnit](./testing-nunit.md).

## Write a test

Replace `[Fact]` or `[Test]` with `[ModuleTest]`. Give the module types to the attribute. Declare a parameter for each service that the test uses.

```csharp
using DependencyModules.xUnit.Attributes;
using Shop;
using Xunit;

namespace Shop.Tests;

public class PriceCalculatorTests
{
    [ModuleTest(typeof(ShopModule))]
    public void TotalMultipliesPriceAndQuantity(IPriceCalculator calculator)
    {
        Assert.Equal(10m, calculator.Total(2.5m, 4));
    }
}
```

## Modules for a test

A test loads the modules from these locations:

- The types in `[ModuleTest]`. Each type must have a constructor without parameters.
- The module attributes on the test method, for example `[ShopModule]`.
- The module attributes on the test class.
- The module attributes on the assembly, for example `[assembly: ShopModule]`.

```csharp
using DependencyModules.xUnit.Attributes;
using Shop;
using Xunit;

namespace Shop.Tests;

[ShopModule]
public class OrderTests
{
    [ModuleTest]
    public void CalculatorIsAvailable(IPriceCalculator calculator)
    {
        Assert.NotNull(calculator);
    }
}
```

A module attribute on the assembly is applicable to all tests of the assembly. A module attribute can also give module parameters, for example `[MailModule("localhost", 25)]`.

The modules load in this sequence:

1. The modules of `[ModuleTest]`, in the sequence of the list.
2. The modules from the assembly.
3. The modules from the test class.
4. The modules from the test method.

Thus a registration from a method attribute is after a registration from a class attribute. When a parameter has more than one registration, it gets the instance from the last registration. Module dependencies can change this sequence. For more information, refer to [Module load sequence](./modules.md#module-load-sequence).

If two locations give modules that are equal, the module loads one time. The test package keeps the instance from the first location in this list: the method, the class, the assembly, and `[ModuleTest]`. The load operation compares modules with `Equals`. For the `Equals` method of a module, refer to [Module equality](./modules.md#module-equality).

## A new service provider for each test

Each test gets a new service collection and a new service provider. A test does not use the service provider of a different test.

- A data row gets a new service provider.
- In NUnit, `[Repeat]` and `[Retry]` run a test more than one time. Each iteration gets a new service provider.
- The test package disposes the service provider. The service provider then disposes the services that it made. NUnit disposes the service provider after each iteration of the test. xUnit disposes the service providers of all data rows after the last row.

## Test parameters

The test package gets a value for each parameter in this sequence:

1. The test package gives the values of the data row to the first parameters.
2. A parameter of type `IServiceProvider` gets the service provider of the test.
3. A parameter attribute that gives values, for example `[Mock]`, gives the value. If a parameter has more than one of these attributes, the first value that is not `null` is the value.
4. A parameter with `[FromKeyedServices("key")]` gets the keyed service from the service provider. If there is no keyed registration, the value is `null`.
5. The service provider gives the service.
6. If the service provider has no registration for the type, the test package makes an instance with `ActivatorUtilities.CreateInstance`. The service provider gives the constructor parameters.

Step 6 lets a test get a class that has no registration, for example the class that the test examines. Step 6 is not applicable to a parameter with `[FromKeyedServices]`.

### Give constructor values: `[InjectValues]`

When the test package makes an instance of a class that has no registration, `[InjectValues]` gives more constructor arguments. The service provider gives the other arguments.

```csharp
using DependencyModules.Testing.Attributes;
using DependencyModules.xUnit.Attributes;
using Shop;
using Xunit;

namespace Shop.Tests;

public class CheckoutReport(IPriceCalculator calculator, string customer)
{
    public string Line(decimal price, int quantity) =>
        $"{customer}: {calculator.Total(price, quantity)}";
}

public class CheckoutReportTests
{
    [ModuleTest(typeof(ShopModule))]
    public void LineContainsTheCustomer([InjectValues("Ada")] CheckoutReport report)
    {
        Assert.StartsWith("Ada:", report.Line(1m, 1));
    }
}
```

## Replace a service for a test: `[TestExport]`

`[TestExport]` registers a service for the tests that it is applicable to. Put it on a test method, on a test class, or on the assembly.

```csharp
using DependencyModules.Testing.Attributes;
using DependencyModules.xUnit.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Shop;
using Xunit;

namespace Shop.Tests;

public class FixedPriceCalculator : IPriceCalculator
{
    public decimal Total(decimal price, int quantity) => 1m;
}

public class ExportTests
{
    [ModuleTest(typeof(ShopModule))]
    [TestExport(
        typeof(IPriceCalculator),
        Implementation = typeof(FixedPriceCalculator),
        Lifetime = ServiceLifetime.Singleton
    )]
    public void UsesTheExport(IPriceCalculator calculator)
    {
        Assert.Equal(1m, calculator.Total(100m, 3));
    }
}
```

| Property | Default | Function |
| --- | --- | --- |
| `Service` | Not applicable | The service type. You give it in the constructor. |
| `Implementation` | The service type | The class that the registration makes. |
| `Lifetime` | `Transient` | The lifetime of the registration. |
| `Shared` | `false` | If the value is `true`, the service providers from `ITestContainerSource` give the same instance of the service. Refer to [More service providers in a test](./testing-container-source.md). |

The test package adds the `[TestExport]` registrations after the modules. Thus they are after the registrations of the modules.

The decorators of the modules do not change a `[TestExport]` registration. The decorators change the registrations when the modules load, before the test package adds the `[TestExport]` registrations. This is also true for `[Mock]`.

## Test information: `ITestCaseInfo`

The service provider of a test contains an `ITestCaseInfo` service. It has the test method, the argument values, and the attributes of the test. Each test package has an `ITestCaseInfo` interface in its `Impl` namespace.

## Environment for a test

To give an environment to a test, write an attribute that implements `IModuleEnvironmentProvider` from `DependencyModules.Runtime.Interfaces`:

```csharp
using System.Reflection;
using DependencyModules.Runtime;
using DependencyModules.Runtime.Interfaces;

namespace Shop.Tests;

[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method)]
public class TestEnvironmentAttribute(string name) : Attribute, IModuleEnvironmentProvider
{
    public IModuleEnvironment? ProvideEnvironment(MethodInfo testMethod) =>
        new ModuleEnvironment(false, name);
}
```

Put the attribute on a test method, a test class, or the assembly. If attributes at more than one level give an environment, the test uses the environment from the method. If the method has no such attribute, the test uses the environment from the class. If the class has no such attribute, the test uses the environment from the assembly. If no attribute gives an environment, the modules use the default environment.

```csharp
using DependencyModules.xUnit.Attributes;
using Shop;
using Xunit;

namespace Shop.Tests;

public class EnvironmentTests
{
    [ModuleTest(typeof(ShopModule))]
    [TestEnvironment("Development")]
    public void RunsInDevelopment(DependencyModules.Runtime.Interfaces.IModuleEnvironment environment)
    {
        Assert.Equal("Development", environment.EnvironmentName);
    }
}
```

## Attributes that you write for tests

The `DependencyModules.Testing.Attributes.Interfaces` namespace has interfaces for attributes that you write. Put an `ITestParameterValueProvider` attribute on a parameter. Put the other attributes on a test method, a test class, or the assembly.

| Interface | Function |
| --- | --- |
| `ITestServiceSetupAttribute` | Adds registrations to the service collection of the test, after the modules. |
| `IServiceProviderBuilderAttribute` | Builds the service provider from the service collection. If there are attributes at more than one level, the test uses only one attribute. It uses the attribute on the method. If the method has no such attribute, it uses the attribute on the class. If the class has no such attribute, it uses the attribute on the assembly. |
| `ITestStartupAttribute` | Runs code after the test package builds the service provider, before the test. |
| `ITestParameterValueProvider` | A parameter attribute that registers services and gives the value of the parameter. `[Mock]` uses it. |

If no `IServiceProviderBuilderAttribute` is applicable, the test package calls `BuildServiceProvider()` without `ServiceProviderOptions`. Thus the service provider does not validate scopes. It also does not validate the registrations when the test package builds it. The example that follows enables these two checks.

The methods of these interfaces get an `ITestMethodContext` value. In xUnit, you can cast this value to `IXunitTestMethodContext`. In NUnit, you can cast it to `INUnitTestMethodContext`.

```csharp
using DependencyModules.Testing.Attributes.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Shop.Tests;

public class ValidatedProviderAttribute : Attribute, IServiceProviderBuilderAttribute
{
    public IServiceProvider BuildServiceProvider(
        ITestMethodContext testMethod,
        IServiceCollection serviceCollection
    ) =>
        serviceCollection.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
}
```

## The steps of a test

For each test, the test package does these steps:

1. Makes a service collection.
2. Registers `ITestCaseInfo` and `ITestContainerSource`.
3. Registers the environment from an `IModuleEnvironmentProvider` attribute, if there is one.
4. Loads the modules.
5. Uses the `ITestServiceSetupAttribute` attributes. It uses the mock support attributes first. Then it uses the other attributes, for example `[TestExport]`.
6. Uses the parameter attributes, for example `[Mock]`.
7. Builds the service provider.
8. Runs the `ITestStartupAttribute` attributes.
9. Gets the parameter values and runs the test.
10. Disposes the service provider. In xUnit, this step occurs after the last data row of the test method.

## More information

- [xUnit](./testing-xunit.md) and [NUnit](./testing-nunit.md) tell you about the test packages.
- [Mocks](./testing-mocking.md) tells you how to replace services with mocks.
- [More service providers in a test](./testing-container-source.md) tells you how to make more service providers in one test.
