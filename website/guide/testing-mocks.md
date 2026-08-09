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

Note what stayed real: `SummaryProvider` was not mocked, so the call still travels
`Weather` → `SummaryProvider` → `IAiSummaryProvider`. Only the leaf was swapped.

::: tip Register the mocking library once
`[Mock]` needs a mocking framework, supplied by a separate package. Pick the one you already use:

| Package | Attribute |
|---|---|
| `DependencyModules.NSubstitute` | `[NSubstituteSupport]` |
| `DependencyModules.Moq` | `[MoqSupport]` |
| `DependencyModules.FakeItEasy` | `[FakeItEasySupport]` |

```shell
dotnet add package DependencyModules.NSubstitute
```

```csharp
[assembly: NSubstituteSupport]
```

Without one, `[Mock]` fails with a message telling you so. Like the module attributes it works at
assembly, class or method level; assembly is almost always right.

With NSubstitute and FakeItEasy the injected instance is also what you configure. Moq separates the
two, so the container gets `Mock<T>.Object` and you reach the mock with `Mock.Get(instance)`:

```csharp
Mock.Get(summaryProvider).Setup(x => x.Summarize(It.IsAny<string>())).Returns("mild");
```
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
