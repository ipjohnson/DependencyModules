# xUnit

`DependencyModules.xUnit` adds `[ModuleTest]` to xUnit v3 test projects. The package is compatible with `xunit.v3` version 3.2.2 and all subsequent versions before 4.0.0.

## Install

```shell
dotnet add package DependencyModules.xUnit
```

Also add references to `xunit.v3` and to a test runner, for example `xunit.runner.visualstudio`.

## `[ModuleTest]`

`[ModuleTest]` is in the `DependencyModules.xUnit.Attributes` namespace. Put it on a test method. Do not also put `[Fact]` or `[Theory]` on the method. `[ModuleTest]` has these constructors:

| Constructor | Result |
| --- | --- |
| `[ModuleTest]` | Loads no module from the attribute. The test can get modules from module attributes. |
| `[ModuleTest(typeof(ShopModule))]` | Loads one module. |
| `[ModuleTest(typeof(ShopModule), typeof(MailModule))]` | Loads the modules in the sequence of the list. |

The constructor with two or more module types does not record the source file and the line of the test. Thus the test explorer of an IDE cannot open the source code of this test. The other two constructors record this information.

`[ModuleTest]` is an xUnit `FactAttribute`. Thus `Skip`, `SkipType`, `SkipUnless`, `SkipWhen`, `SkipExceptions`, `Explicit`, `Timeout`, `DisplayName`, and traits have the same function as on `[Fact]`.

```csharp
using DependencyModules.xUnit.Attributes;
using Shop;
using Xunit;

namespace Shop.Tests;

public class CalculatorTests
{
    [ModuleTest(typeof(ShopModule))]
    public void Multiplies(IPriceCalculator calculator)
    {
        Assert.Equal(6m, calculator.Total(2m, 3));
    }

    [ModuleTest(typeof(ShopModule), Skip = "Not ready")]
    public void SkippedTest(IPriceCalculator calculator) { }
}
```

The service provider gives values only to the parameters of the test method. xUnit gives the values for the constructor of the test class.

## Data rows

You can use xUnit data attributes with `[ModuleTest]`, for example `[InlineData]`, `[MemberData]`, and `[ClassData]`. The test package also reads other attributes that implement the xUnit `IDataAttribute` interface. The test package gives the values of a row to the first parameters of the method. The service provider gives the values for the other parameters.

```csharp
using DependencyModules.xUnit.Attributes;
using Shop;
using Xunit;

namespace Shop.Tests;

public class RowTests
{
    [ModuleTest(typeof(ShopModule))]
    [InlineData(2, 4)]
    [InlineData(3, 6)]
    public void RowAndService(int quantity, int expected, IPriceCalculator calculator)
    {
        Assert.Equal(expected, calculator.Total(2m, quantity));
    }
}
```

Each row gets a new service provider. If the data attributes give no rows, the test fails. It does not pass when no row runs.

A row of the type `TheoryData<T>` or `TheoryDataRow<T>` has a type for each value. The xUnit analyzer compares the number of these types with the number of parameters. If the number of types is less, the analyzer gives the error xUnit1037. For a `[ModuleTest]` method, this number of values is correct.

Disable xUnit1037 for these tests:

```csharp
using DependencyModules.xUnit.Attributes;
using Shop;
using Xunit;

namespace Shop.Tests;

public class TypedRowTests
{
    public static TheoryData<int> Quantities => new(2, 3);

#pragma warning disable xUnit1037

    [ModuleTest(typeof(ShopModule))]
    [MemberData(nameof(Quantities))]
    public void TypedRow(int quantity, IPriceCalculator calculator)
    {
        Assert.Equal(2m * quantity, calculator.Total(2m, quantity));
    }

#pragma warning restore xUnit1037
}
```

A `TheoryDataRow` can set its `Skip`, `SkipType`, `SkipUnless`, `SkipWhen`, `Timeout`, `Traits`, `TestDisplayName`, and `Label`. These values are applicable only to that row.

The test of a data row gets only the traits of the row, for example from the `Traits` property of `[InlineData]`. It does not get the traits of the method or the class.

## Lifetime of the service provider

The test package builds the service providers when xUnit makes the tests of a test method. After all tests of the test method run, the test package disposes these service providers.

## Test information

The `ITestCaseInfo` interface in the `DependencyModules.xUnit.Impl` namespace has these properties:

| Property | Value |
| --- | --- |
| `TestMethod` | The xUnit `IXunitTestMethod`. |
| `TestMethodArguments` | The values of the parameters. |
| `TestMethodAttributes` | The attributes of the test method, the test class, and the assembly. |

```csharp
using DependencyModules.xUnit.Attributes;
using DependencyModules.xUnit.Impl;
using Xunit;

namespace Shop.Tests;

public class InfoTests
{
    [ModuleTest]
    public void KnowsItsName(ITestCaseInfo info)
    {
        Assert.Equal(nameof(KnowsItsName), info.TestMethod.MethodName);
    }
}
```
