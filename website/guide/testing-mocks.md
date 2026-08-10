# Mocks and values

## The problem

A provider built from your real modules gives you real services, which is usually the point — and
occasionally the problem. Two of the services behind `Weather` are non-deterministic:

```csharp
[SingletonService]
public class TemperatureProvider : ITemperatureProvider {
    public int GetTemperature() => Random.Shared.Next(-20, 55);
}
```

You cannot assert on a forecast built out of random numbers. But you do not want to abandon the
container either — `Weather` and `SummaryProvider` should still be the real ones, wired the real way.
You want to replace exactly two leaves of the graph and leave the rest alone.

## How DependencyModules helps

Mark the parameter `[Mock]` and that service is **replaced in the container** before anything is
resolved. Everything constructed afterwards gets the substitute:

```csharp
[ModuleTest]
public void GetStaticForecast(
    Weather weather,
    [Mock] ITemperatureProvider temperatureProvider,
    [Mock] IAiSummaryProvider aiSummaryProvider) {

    temperatureProvider.GetTemperature().Returns(38);
    aiSummaryProvider.GetSummary().Returns("Sunny");

    var forecast = weather.GetWeatherForecast().ToArray();

    Assert.All(forecast, day => Assert.Equal(38, day.TemperatureC));
    Assert.All(forecast, day => Assert.Equal("Sunny", day.Summary));
}
```

`Weather` is still constructed by the container, and it receives the same substitutes the test is
holding. You wire nothing together yourself.

`[Mock]` comes from `DependencyModules.Testing`, which your test framework integration already brings
in — so it needs a `using DependencyModules.Testing.Attributes;` alongside the one for
`[ModuleTest]`. It carries no test framework dependency of its own, which is why it lives there and
not in `DependencyModules.xUnit`.

Note what stayed real: `SummaryProvider` was not mocked, so the call still travels
`Weather` → `SummaryProvider` → `IAiSummaryProvider`. Only the leaf was swapped.

## Choosing a mocking library

`[Mock]` does not depend on a particular mocking library. It defines a seam, and a small package
fills it — so use whichever library you already have:

| Package | Attribute |
|---|---|
| `DependencyModules.NSubstitute` | `[NSubstituteSupport]` |
| `DependencyModules.Moq` | `[MoqSupport]` |
| `DependencyModules.FakeItEasy` | `[FakeItEasySupport]` |

Install one and apply its attribute. Like the module attributes it works at assembly, class or
method level, and assembly is usually right:

```shell
dotnet add package DependencyModules.Moq
```

```csharp
[assembly: MoqSupport]
```

Without one, `[Mock]` fails with a message telling you so.

### The same test in each

Only the configuration lines differ — `[Mock]`, the injection and the assertions are identical. The
example above is NSubstitute; here are the other two:

::: code-group

```csharp [NSubstitute]
temperatureProvider.GetTemperature().Returns(38);
aiSummaryProvider.GetSummary().Returns("Sunny");
```

```csharp [Moq]
Mock.Get(temperatureProvider).Setup(x => x.GetTemperature()).Returns(38);
Mock.Get(aiSummaryProvider).Setup(x => x.GetSummary()).Returns("Sunny");
```

```csharp [FakeItEasy]
A.CallTo(() => temperatureProvider.GetTemperature()).Returns(38);
A.CallTo(() => aiSummaryProvider.GetSummary()).Returns("Sunny");
```

:::

Unconfigured members return `default` rather than throwing, in all three.

### Moq: ask for the `Mock<T>` instead

NSubstitute and FakeItEasy hand you an object that *is* the mock, so the parameter the container
injected is the thing you configure. Moq keeps the two apart, which is why the version above needs
`Mock.Get`.

You can skip that by naming the mock in the parameter type. No `[Mock]` — the type already says what
it is:

```csharp
[ModuleTest]
public void GetStaticForecast(
    Weather weather,
    Mock<ITemperatureProvider> temperatureProvider,
    Mock<IAiSummaryProvider> aiSummaryProvider) {

    temperatureProvider.Setup(x => x.GetTemperature()).Returns(38);
    aiSummaryProvider.Setup(x => x.GetSummary()).Returns("Sunny");

    var forecast = weather.GetWeatherForecast().ToArray();

    Assert.All(forecast, day => Assert.Equal(38, day.TemperatureC));
}
```

This does the same thing `[Mock]` does — `ITemperatureProvider` is replaced in the container before
anything is resolved, so `Weather` is built against the same mock. You just hold the `Mock<T>` rather
than the object, and `mock.Object` gets you the object when you want it.

Both spellings can appear on one test, and they agree: ask for `[Mock] ITemperatureProvider` and
`Mock<ITemperatureProvider>` together and you get one mock seen two ways, not two mocks. Two
parameters naming the same `Mock<T>` are likewise one mock.

`[Mock]` on a `Mock<T>` parameter is allowed and does nothing — the type is already enough.

::: warning
A `Mock<T>` parameter only means anything when `[MoqSupport]` is in scope. Without it the parameter
still resolves — the container constructs a `Mock<T>` like any other concrete type — but nothing
registers it, so the service under test gets the real implementation and your setups apply to a mock
nobody can see.
:::

## When you want a real object, not a mock

A mock is right when you intend to **assert on the interaction** — what was called, with which
arguments. When you instead want a working implementation that simply behaves differently, a mock
makes you stub out every member you touch.

`[TestExport]` registers a real type into the test's container without touching the module:

```csharp
public class FixedClock : IClock {
    public DateTime UtcNow => new(2026, 1, 1);
}

[ModuleTest]
[TestExport(typeof(IClock), Implementation = typeof(FixedClock), Lifetime = ServiceLifetime.Singleton)]
public void OrdersAreStampedWithTheCurrentTime(IOrderService service) { }
```

`FixedClock` is constructed by the container, so it can have dependencies of its own.

| Property | |
|---|---|
| *(constructor)* | the service type |
| `Implementation` | defaults to the service type when omitted |
| `Lifetime` | defaults to `Transient` |

It also applies at assembly, class or method level, so a stub every test needs can sit in your
bootstrap file once.

## When the parameter is not a service at all

Sometimes a test parameter is data — a string, an id, a record combining both. `[InjectValues]`
supplies the parts the container cannot:

```csharp
public record InjectModel(IDependencyOne DependencyOne, string StringValue);

[ModuleTest]
public void InjectTestValue([InjectValues("Hello World!")] InjectModel model) {
    Assert.NotNull(model.DependencyOne);                // resolved from the container
    Assert.Equal("Hello World!", model.StringValue);    // supplied by the attribute
}
```

The values are matched against the constructor parameters the container **cannot** supply, so you
list only what it could not work out for itself.

## Choosing between the three

| | Reach for it when |
|---|---|
| `[Mock]` | you want to assert on the interaction — what was called, with what |
| `[TestExport]` | you want a real object with different behaviour, constructed by the container |
| `[InjectValues]` | the parameter is data, not a service |

## A trap worth knowing about

An [intercepted](/guide/interception) service resolves as a **generated wrapper**, not as your class.
So this fails, confusingly:

```csharp
Assert.IsType<Orders>(provider.GetRequiredService<IOrders>());   // it is Orders_Intercepted
```

Assert on the interface, or on behaviour. The same applies to a [decorated](/guide/decorators)
service, where what resolves is the outermost decorator.
