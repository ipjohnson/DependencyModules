# `CurrentTest` and test output

`CurrentTest` gives information about the test that runs. Use it in code that does not get the test as a parameter, for example an assertion helper or a logger. This code does not need a reference to a test framework. `CurrentTest` is in the `DependencyModules.Testing.Impl` namespace of the `DependencyModules.Testing` package.

## Members

| Member | Value |
| --- | --- |
| `Key` | The object of the test framework for the test that runs. Compare it by reference. Two tests that run at the same time have different keys. |
| `DisplayName` | The display name of the test that runs. |
| `Assembly` | The assembly that contains the class of the test. |
| `TryWriteLine(message)` | Writes a line to the output of the test. |
| `Provider` | The `ICurrentTestProvider` that gives these values. |

If no test runs, `Key`, `DisplayName`, and `Assembly` are `null`. Also, `TryWriteLine` writes nothing and returns `false`.

The test framework keeps the `Key` object until the test ends. Thus you can keep data for each test in a `ConditionalWeakTable` with `Key` as the key.

```csharp
using System.Runtime.CompilerServices;
using DependencyModules.Testing.Impl;

namespace Shop.Tests;

public static class LastOrder
{
    private static readonly ConditionalWeakTable<object, string> Orders = new();

    public static void Record(string orderId)
    {
        if (CurrentTest.Key is { } test)
        {
            Orders.AddOrUpdate(test, orderId);
        }
    }

    public static string? Get() =>
        CurrentTest.Key is { } test && Orders.TryGetValue(test, out var orderId) ? orderId : null;
}
```

## The test packages

Each test package sets `CurrentTest.Provider` before its first test runs. Thus `CurrentTest` operates with all the test packages.

| Package | Source of the values |
| --- | --- |
| `DependencyModules.xUnit` and `DependencyModules.xUnit4` | `TestContext.Current` of xUnit |
| `DependencyModules.NUnit` | `TestExecutionContext.CurrentContext` of NUnit |

A test package does not replace a provider that is already set.

## Differences between the test frameworks

In xUnit, the test package builds the service provider before xUnit starts the test. Thus, in an `ITestServiceSetupAttribute` or an `ITestStartupAttribute`, `Key` and `DisplayName` are `null`. `TryWriteLine` returns `false`, but `Assembly` has a value.

In NUnit, the test package builds the service provider in the test. Thus all values are available in these attributes.

In NUnit, all iterations of a `[Repeat]` or `[Retry]` test have the same `Key`. NUnit does not make a different object for each iteration. Each iteration still gets a new service provider.

## Write the log to the test output

`TestOutputLoggerProvider` is an `ILoggerProvider` that writes each log entry to the output of the test that runs. It is in the `DependencyModules.Testing.Impl` namespace. Register it in the service collection of the test. Then the logs of your application show in the output of the test.

```csharp
using DependencyModules.Testing.Attributes.Interfaces;
using DependencyModules.Testing.Impl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Shop.Tests;

[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method)]
public class TestLoggingAttribute : Attribute, ITestServiceSetupAttribute
{
    public void SetupServiceCollection(
        ITestMethodContext testMethod,
        IServiceCollection serviceCollection
    ) =>
        serviceCollection.AddSingleton<ILoggerProvider>(new TestOutputLoggerProvider());
}
```

Put `[assembly: TestLogging]` in the test project to use the logger for all tests. The logger factory writes to all `ILoggerProvider` services in the service provider. Thus the other providers also get each entry.

Each entry is one line, in the single-line format of the console logger:

```text
info: Shop.OrderService[0] Order 42 accepted
```

If the entry has an exception, the exception is on the subsequent lines. If no test runs, the logger does not write the entry. For example, this occurs when a background task writes an entry after its test ends.

The default constructor uses `CurrentTest.Provider`. To use a different `ICurrentTestProvider`, give it to the constructor.
