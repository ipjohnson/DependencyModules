# NUnit

`DependencyModules.NUnit` adds `[ModuleTest]` and `[ModuleTestCase]` to NUnit test projects. The package is compatible with NUnit version 4.2.2 and all subsequent versions before 5.0.0.

## Install

```shell
dotnet add package DependencyModules.NUnit
```

Also add references to `NUnit3TestAdapter` and to `Microsoft.NET.Test.Sdk`.

## `[ModuleTest]`

`[ModuleTest]` is in the `DependencyModules.NUnit.Attributes` namespace. Put it on a test method. Do not also put `[Test]` on the method. Give zero or more module types in the constructor.

```csharp
using DependencyModules.NUnit.Attributes;
using NUnit.Framework;
using Shop;

namespace Shop.NUnitTests;

public class CalculatorTests
{
    [ModuleTest(typeof(ShopModule))]
    public void Multiplies(IPriceCalculator calculator)
    {
        Assert.That(calculator.Total(2m, 3), Is.EqualTo(6m));
    }
}
```

A class with `[ModuleTest]` methods is a test fixture. `[TestFixture]` is not necessary on the class.

NUnit builds and runs the test with its usual test commands. Thus `[SetUp]`, `[TearDown]`, `[Timeout]`, `[Repeat]`, and `[Retry]` have their usual function.

## Data rows: `[ModuleTestCase]`

For data rows, use `[ModuleTestCase]`. Each attribute is one row. The test package gives the values of a row to the first parameters of the method. The service provider gives the values for the other parameters.

```csharp
using DependencyModules.NUnit.Attributes;
using NUnit.Framework;
using Shop;

namespace Shop.NUnitTests;

public class RowTests
{
    [ModuleTest(typeof(ShopModule))]
    [ModuleTestCase(2, 4)]
    [ModuleTestCase(3, 6, TestName = "Three items")]
    public void RowAndService(int quantity, int expected, IPriceCalculator calculator)
    {
        Assert.That(calculator.Total(2m, quantity), Is.EqualTo((decimal)expected));
    }
}
```

`TestName` sets the name of the row of its `[ModuleTestCase]`. Without `TestName`, the name is the method name and the values, for example `RowAndService(2, 4)`. The rows from a different row source also get this default name.

The number of values in a row can be less than the number of parameters. A row with more values than parameters does not run. NUnit then shows the row as `NotRunnable` and gives the cause.

Do not use `[TestCase]`, `[TestCaseSource]`, `[Values]`, or `[Range]` with `[ModuleTest]`. NUnit makes more tests from these attributes, and the test package cannot give their values to the parameters. Thus the test package does not run these tests. NUnit shows each of them as `NotRunnable`, and the message gives the name of the attribute. This is also true for the other NUnit data attributes, for example `[Random]` and `[ValueSource]`.

NUnit makes some of these tests `NotRunnable` before the test package can examine them. An example is a `[TestCaseSource]` row whose number of values is less than the number of parameters. For such a test, NUnit shows its own message.

To get rows from a different source, write an attribute that implements `IModuleTestDataAttribute`. Its `GetRows(MethodInfo method)` method gives the rows. Put the attribute on the test method.

## Each iteration gets a new service provider

The test package builds a new service provider for each row and for each iteration from `[Repeat]` and `[Retry]`.

The `[SetUp]` and `[TearDown]` methods run after the test package builds the service provider of the iteration. After `[TearDown]`, the test package disposes the service provider. A `[SetUp]` method cannot get the service provider or `ITestCaseInfo`. Only the parameters of the test method get values from the service provider.

## Test information

The `ITestCaseInfo` interface in the `DependencyModules.NUnit.Impl` namespace has these properties:

| Property | Value |
| --- | --- |
| `TestMethod` | The NUnit `TestMethod`. |
| `TestMethodArguments` | The values of the parameters. |
| `TestMethodAttributes` | The attributes of the test method, the test class, and the assembly. |
