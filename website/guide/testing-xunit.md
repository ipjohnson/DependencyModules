# xUnit

`DependencyModules.xUnit` is the xUnit integration. Read [Testing modules](/guide/testing) first —
this page covers only what is specific to xUnit.

```shell
dotnet add package DependencyModules.xUnit
```

```csharp
using DependencyModules.xUnit.Attributes;

public class WeatherTests {
    [ModuleTest]
    [ApplicationModule]
    public void GetForecast(Weather weather) {
        var forecast = weather.GetWeatherForecast().ToArray();

        Assert.Equal(5, forecast.Length);
    }
}
```

Requires **xUnit v3**. `[ModuleTest]` derives from `FactAttribute` and is discovered through xUnit's
own test case discoverer, so it is a fact as far as the rest of xUnit is concerned.

## `[ModuleTest]` replaces `[Fact]`

It replaces `[Theory]` as well. A module test with data attributes on it produces one test case per
row without your saying so — there is no separate attribute for the parameterised case.

Because it derives from `FactAttribute`, everything `[Fact]` carries carries here too:

```csharp
[ModuleTest(Skip = "flaky on CI", Explicit = true, Timeout = 5000, DisplayName = "Forecast")]
public void GetForecast(Weather weather) { }
```

`Skip`, `SkipUnless`, `SkipWhen`, `SkipExceptions`, `SkipType`, `Explicit`, `Timeout` and
`DisplayName` all behave as xUnit defines them, and `[Trait]` is carried onto the generated test
cases.

## Naming modules on the attribute

Beyond the module attributes described in [Testing modules](/guide/testing#stop-repeating-the-module-list),
`[ModuleTest]` takes module types directly:

```csharp
[ModuleTest(typeof(ApplicationModule))]
public void GetForecast(Weather weather) { }
```

::: warning Two or more modules loses the source location
`[ModuleTest]` captures the file and line it sits on through `[CallerFilePath]`/`[CallerLineNumber]`,
which is how a test explorer navigates back to your test. C# will not accept caller-info parameters
after a `params` array, so the overload taking **several** module types cannot capture them.

Such a test still runs and still reports correctly; only navigation from the explorer to the source
is unavailable. Naming one module, or none, takes an overload that keeps it — so prefer the module
attributes for the multi-module case:

```csharp
[ModuleTest]                        // location captured
[ApplicationModule]
[DiagnosticsModule]
public void GetForecast(Weather weather) { }
```
:::

## Data-driven tests

Any xUnit data attribute works — `[InlineData]`, `[MemberData]`, `[ClassData]`, and anything else
implementing `IDataAttribute`. Row arguments come first, injected ones after:

```csharp
[ModuleTest]
[InlineData("one")]
[InlineData("two")]
public void MultipleRows(string value, ITemperatureProvider provider) {
    Assert.NotNull(value);       // from [InlineData]
    Assert.NotNull(provider);    // from the container
}
```

A row supplies the **leading** parameters and may supply fewer than the method takes — that is the
point of it. The rest are resolved from the container.

Each row is a separate test case with its own container, so state cannot carry from one row to the
next.

`TheoryDataRow`'s own metadata is honoured per row, so a single row can skip or carry its own traits:

```csharp
public static TheoryData<string> Cases => new() {
    new TheoryDataRow<string>("ok"),
    new TheoryDataRow<string>("broken") { Skip = "pending #412" },
};

[ModuleTest]
[MemberData(nameof(Cases))]
public void MultipleRows(string value, ITemperatureProvider provider) { }
```

## Reading the test case

`ITestCaseInfo` is resolvable from the container and exposes xUnit's own metadata for the running
test:

```csharp
[ModuleTest]
public void KnowsWhatItIs(ITestCaseInfo testCase) {
    IXunitTestMethod method = testCase.TestMethod;

    Assert.Equal(nameof(KnowsWhatItIs), method.MethodName);
}
```

| Member | |
|---|---|
| `TestMethod` | the `IXunitTestMethod` xUnit built |
| `TestMethodArguments` | the arguments the test will be invoked with |
| `TestMethodAttributes` | every attribute on the method |

## Fixtures and lifetime

xUnit constructs the test class **once per test**, which is its own model and unchanged here. Combined
with a container per test, that means nothing survives between tests unless you deliberately make it —
a class fixture, a collection fixture, or a static.

The container's lifetime brackets the test, so a constructor or `IAsyncLifetime` on the class runs
inside it. Anything the test class needs from the container has to come through a `[ModuleTest]`
parameter, though — xUnit constructs the class, not this package, so a constructor parameter is
xUnit's to supply.

## Customising how the provider is built

Implement `IServiceProviderBuilderAttribute` to take over the final step, if you want validation on or
a different container:

```csharp
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Assembly)]
public class ValidatingProviderAttribute : Attribute, IServiceProviderBuilderAttribute {
    public IServiceProvider BuildServiceProvider(
        ITestMethodContext testMethod, IServiceCollection serviceCollection) =>
        serviceCollection.BuildServiceProvider(new ServiceProviderOptions {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
}
```

It runs last, after every other hook has contributed, so it is also the final chance to amend the
collection. Without one, the collection is built with `BuildServiceProvider()` and its defaults.

Unlike the other hooks, which all contribute, only **one** of these is used. Declare a single one —
assembly level is the usual place, since replacing the container is a project-wide decision.

This one is not xUnit-specific — it lives in `DependencyModules.Testing` and works the same under
[NUnit](/guide/testing-nunit).

## Next

- [Mocking frameworks](/guide/testing-mocking) — `[Mock]` and the three libraries behind it
- [NUnit](/guide/testing-nunit) — the same integration for NUnit
- [Testing registrations](/guide/testing-registrations) — asserting on what a module registered
