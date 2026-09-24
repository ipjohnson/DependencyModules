# Mocks

A mock replaces a service in the service provider of a test. The test gets the mock as a parameter and sets the return values of its members. The other services get the mock in their constructors.

## Packages

| Package | Attribute | Mock library |
| --- | --- | --- |
| `DependencyModules.NSubstitute` | `[NSubstituteSupport]` | NSubstitute |
| `DependencyModules.Moq` | `[MoqSupport]` | Moq |
| `DependencyModules.FakeItEasy` | `[FakeItEasySupport]` | FakeItEasy |

Add one of these packages and a test package to the test project. Put the attribute of the mock package on the test method, on the test class, or on the assembly. An attribute on the assembly is applicable to all tests:

```csharp
using DependencyModules.NSubstitute;

[assembly: NSubstituteSupport]
```

If more than one mock support attribute is applicable to a test, `[Mock]` uses only one attribute. It uses the attribute on the method. If the method has no such attribute, it uses the attribute on the class. If the class has no such attribute, it uses the attribute on the assembly. Each applicable `[MoqSupport]` attribute also registers the `Mock<T>` parameters.

## `[Mock]`

Put `[Mock]` on a test parameter. `[Mock]` is in the `DependencyModules.Testing.Attributes` namespace.

```csharp
using DependencyModules.NSubstitute;
using DependencyModules.Testing.Attributes;
using DependencyModules.xUnit.Attributes;
using NSubstitute;
using Xunit;

namespace Weather.Tests;

public interface ITemperatureSource
{
    int Celsius();
}

public class Forecast(ITemperatureSource source)
{
    public string Describe() => source.Celsius() > 25 ? "hot" : "mild";
}

[NSubstituteSupport]
public class ForecastTests
{
    [ModuleTest]
    public void HotAbove25([Mock] ITemperatureSource source, Forecast forecast)
    {
        source.Celsius().Returns(30);

        Assert.Equal("hot", forecast.Describe());
    }
}
```

For each `[Mock]` parameter, the test package does these steps:

1. Makes a mock of the parameter type with the mock library.
2. Registers the mock as a singleton, after the modules and after `[TestExport]`.
3. Gives the mock to the parameter.

Because the mock registration is the last registration, the service provider gives the mock to all services that get the type. If the parameter has `[FromKeyedServices("key")]`, the registration is a keyed registration with that key.

The NSubstitute and FakeItEasy packages make the mocks with the default configuration of the library: `Substitute.For` and `FakeItEasy.Sdk.Create.Fake`. The parameter and the other services get the same object. Thus the configuration that the test makes on the parameter is applicable to the other services.

Each test, each data row, and each iteration gets new mocks.

If no mock support attribute is applicable to the test, the test fails with the message "Mock library not found".

::: info NOTE
A mock support attribute does not make mocks for services that have no registration. Only the `[Mock]` parameters and, for Moq, the `Mock<T>` parameters get mocks. A service with a dependency that has no registration and no mock parameter causes an error when the test gets the service.
:::

## Moq

If `[MoqSupport]` is applicable to the test, the test can have a `Mock<T>` parameter. The parameter gets the `Mock<T>` object. The service provider gives `mock.Object` for `T`.

```csharp
using DependencyModules.Moq;
using DependencyModules.xUnit.Attributes;
using Moq;
using Xunit;

namespace Weather.Tests;

[MoqSupport]
public class MoqForecastTests
{
    [ModuleTest]
    public void MildAt20(Mock<ITemperatureSource> source, Forecast forecast)
    {
        source.Setup(temperature => temperature.Celsius()).Returns(20);

        Assert.Equal("mild", forecast.Describe());
    }
}
```

This list gives information about the Moq package:

- `Mock<T>` and `[Mock] Mock<T>` give the same result.
- `[Mock] T` gives `mock.Object`. `Mock.Get(value)` gives the `Mock<T>` for the value.
- If a test has a `[Mock] T` parameter and a `Mock<T>` parameter, the two parameters use one mock.
- Two `Mock<T>` parameters for the same `T` get the same mock.
- The package makes each mock with `new Mock<T>()`. If you do not set a member, the member gives the default value of Moq. For example, a member gives an empty array, an empty sequence, or a completed task. A member with a different reference type, for example `string`, gives `null`.

If `[MoqSupport]` is not applicable to the test, a `Mock<T>` parameter gets a new `Mock<T>` from `ActivatorUtilities`. But the service provider does not give `mock.Object` for `T`. The other services then get the usual implementation of `T`.

## Mocks and `[TestExport]`

The test package registers a `[Mock]` parameter after `[TestExport]`. Thus the mock replaces a `[TestExport]` registration of the same service type. A `[Mock]` parameter with `[FromKeyedServices]` does not replace a `[TestExport]` registration without a key.

For Moq, the test package registers a `Mock<T>` parameter before `[TestExport]`. Thus a `[TestExport]` for `T` replaces the registration of `mock.Object`. The `Mock<T>` parameter gets its mock. But the other services get the `[TestExport]` service, not `mock.Object`. If the test also has a `[Mock] T` parameter, this parameter also gets the `[TestExport]` service.

If the test project references `DependencyModules.SourceGenerator`, the generator examines the test methods. If a test method has `[TestExport]` and a `[Mock]` parameter for the same service type, the generator gives the warning DM0021. The generator also gives DM0021 for the two exceptions in this section.

To set a default for many tests, put `[TestExport]` on the class or on the assembly. A `[Mock]` parameter can then replace the default in one test.

## Other mock libraries

To use a different mock library, write an attribute that implements `IMockSupportAttribute` from `DependencyModules.Testing.Attributes.Interfaces`:

| Member | Function |
| --- | --- |
| `object ProvideMock(Type type)` | Gives a new mock of the type. |
| `bool RegistersService(ITestMethodContext testMethod, Type serviceType)` | Gives `true` if the attribute registers the type. `[Mock]` then does not register a mock for this type. The default implementation gives `false`. |

```csharp
using DependencyModules.Testing.Attributes.Interfaces;

namespace Weather.Tests;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Assembly)]
public class StubSupportAttribute : Attribute, IMockSupportAttribute
{
    public object ProvideMock(Type type) =>
        throw new NotSupportedException($"No stub for {type.Name}");
}
```
