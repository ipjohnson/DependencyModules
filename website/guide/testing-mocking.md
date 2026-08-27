# Mocking frameworks

## The problem

A provider built from your real modules gives you real services, which is usually the point — and
occasionally the problem. One of the services behind `Weather` is non-deterministic:

```csharp
[SingletonService]
public class TemperatureProvider : ITemperatureProvider {
    public int GetTemperature() => Random.Shared.Next(-20, 55);
}
```

You cannot assert on a forecast built out of random numbers. But you do not want to abandon the
container either — `Weather` and `SummaryProvider` should still be the real ones, wired the real way.
You want to replace exactly one leaf of the graph and leave the rest alone.

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

`[Mock]` comes from `DependencyModules.Testing`, which your [test framework
integration](/guide/testing#pick-an-integration) already brings in — so it needs a
`using DependencyModules.Testing.Attributes;`. It carries no test framework dependency and no mocking
library dependency of its own.

## Choosing a library

`[Mock]` does not depend on a particular mocking library. It defines a seam, and a small package
fills it — so use whichever library you already have:

| Package | Attribute | Creates |
|---|---|---|
| `DependencyModules.NSubstitute` | `[NSubstituteSupport]` | `Substitute.For(type)` |
| `DependencyModules.Moq` | `[MoqSupport]` | `Mock<T>` |
| `DependencyModules.FakeItEasy` | `[FakeItEasySupport]` | `Sdk.Create.Fake(type)` |

Install one and apply its attribute. Like the module attributes it works at assembly, class or
method level, and assembly is usually right:

```shell
dotnet add package DependencyModules.Moq
```

```csharp
[assembly: MoqSupport]
```

Without one, `[Mock]` throws with a message telling you so, rather than quietly handing back the real
service.

All three work under both [xUnit](/guide/testing-xunit) and [NUnit](/guide/testing-nunit) — the
mocking package and the test framework package are independent choices.

::: tip Pick one per project
The support attributes are found by walking method, class then assembly, and the first one found
supplies the test's mocks. Two in scope is not an error, but which one wins depends on where each is
declared, which is not a thing to rely on.
:::

## The same test in each

Only the configuration lines differ — `[Mock]`, the injection and the assertions around them are
identical. The example above is NSubstitute; here are all three side by side:

::: code-group

```csharp [NSubstitute]
// arrange
temperatureProvider.GetTemperature().Returns(38);

// assert on the interaction
temperatureProvider.Received().GetTemperature();
temperatureProvider.Received(1).Record(Arg.Any<string>());
```

```csharp [Moq]
// arrange
Mock.Get(temperatureProvider).Setup(x => x.GetTemperature()).Returns(38);

// assert on the interaction
Mock.Get(temperatureProvider).Verify(x => x.GetTemperature());
Mock.Get(temperatureProvider).Verify(x => x.Record(It.IsAny<string>()), Times.Once);
```

```csharp [FakeItEasy]
// arrange
A.CallTo(() => temperatureProvider.GetTemperature()).Returns(38);

// assert on the interaction
A.CallTo(() => temperatureProvider.GetTemperature()).MustHaveHappened();
A.CallTo(() => temperatureProvider.Record(A<string>._)).MustHaveHappenedOnceExactly();
```

:::

Mocks are **loose** in all three: an unconfigured member returns `default` rather than throwing. That
is each library's own default, kept rather than overridden.

## NSubstitute

The substitute is both what gets injected and what you configure, so a `[Mock]` parameter can be set
up directly:

```csharp
using DependencyModules.NSubstitute;

[assembly: NSubstituteSupport]
```

```csharp
[ModuleTest]
public void SendsTheMail(IEmailSender sender, [Mock] IAuditLog log) {
    sender.Send("someone@example.com");

    log.Received().Write(Arg.Any<string>());
}
```

Nothing else to know — the parameter is the substitute.

## FakeItEasy

Same shape. The fake is what gets injected and what you configure, through `A.CallTo`:

```csharp
using DependencyModules.FakeItEasy;

[assembly: FakeItEasySupport]
```

```csharp
[ModuleTest]
public void SendsTheMail(IEmailSender sender, [Mock] IAuditLog log) {
    sender.Send("someone@example.com");

    A.CallTo(() => log.Write(A<string>._)).MustHaveHappened();
}
```

Fakes are built through `FakeItEasy.Sdk.Create.Fake(type)` rather than `A.Fake<T>()`, because the
type is not known until the test asks for it. The result is the same object either would produce.

## Moq

Moq is the one that needs a paragraph, because it keeps the mock and the object it produces apart.
`[Mock] IFoo` gives you the **object**, so configuring it means going back through `Mock.Get`:

```csharp
[ModuleTest]
public void SendsTheMail(IEmailSender sender, [Mock] IAuditLog log) {
    Mock.Get(log).Verify(x => x.Write(It.IsAny<string>()));
}
```

### Ask for the `Mock<T>` instead

You can skip that by naming the mock in the parameter type. No `[Mock]` needed — the type already
says what it is:

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
anything is resolved, so `Weather` is built against the same mock. Both the `Mock<T>` and its
`Object` are registered, which is what lines the two halves up: you hold the mock, and everything the
container builds gets its object. `mock.Object` reaches the object yourself when you want it.

### The two spellings agree

Ask for `[Mock] ITemperatureProvider` and `Mock<ITemperatureProvider>` on one test and you get **one
mock seen two ways**, not two mocks. Two parameters naming the same `Mock<T>` are likewise one mock.

`[Mock]` on a `Mock<T>` parameter is allowed and does nothing — the type is already enough.

::: warning `Mock<T>` without `[MoqSupport]` silently does nothing useful
A `Mock<T>` parameter only means anything when `[MoqSupport]` is in scope. Without it the parameter
still resolves — the container constructs a `Mock<T>` like any other concrete type — but nothing
registers it, so the service under test gets the real implementation and your setups apply to a mock
nobody can see.
:::

## What wins when two things register the same service {#precedence}

**The narrowest declaration decides.** `[Mock]` sits on a parameter and names one argument;
`[TestExport]` applies to a method, a class or an assembly. So a `[Mock]` parameter overrides a
`[TestExport]` naming the same service, and that is the shape having both is for — the class sets the
default and one test opts out:

```csharp
[TestExport(typeof(IClock), Implementation = typeof(SystemClock))]   // the fixture default
public class ExpiryTests {

    [ModuleTest]
    public void UsesTheRealClock(IClock clock) { }                   // SystemClock

    [ModuleTest]
    public void Expires([Mock] IClock clock) { }                     // the mock
}
```

Written on the **same method** the two are contradictory rather than useful — the parameter wins and
the `[TestExport]` beside it does nothing — so that is
[DM0021](/reference/diagnostics#dm0021).

`Mock<T>` deliberately does not get this. Asking for the mock object is not the same as declaring the
service mocked, so a `[TestExport]` still beats it:

```csharp
[ModuleTest]
[TestExport(typeof(IClock), Implementation = typeof(SystemClock))]
public void RealClock(IClock clock, Mock<IClock> mock) {
    Assert.IsType<SystemClock>(clock);      // the export won
}
```

Underneath, registrations are last-one-wins and the order is fixed:

1. **Mock support** registers the `Mock<T>` pairs.
2. **`[TestExport]`** and other setup attributes.
3. **`[Mock]` parameters**, last — which is what makes them win.

A `[Mock]` stands aside for a type the mock library registers itself, which is what keeps
`[Mock] IFoo` and `Mock<IFoo>` on one test resolving to a matched pair rather than two unrelated
mocks.

::: warning This changed in 1.2.0
Before 1.2.0 `[TestExport]` won everywhere, including for a `[Mock]`-decorated parameter — so a test
declaring `[Mock] IFoo` alongside a `[TestExport]` for `IFoo` silently held the real implementation,
and the first arrange line threw a mocking-library error naming neither attribute. If you were
relying on that, drop the `[Mock]`.
:::

## When not to mock

A mock is right when you intend to **assert on the interaction** — what was called, with which
arguments. When you want a working implementation that simply behaves differently, a mock makes you
stub out every member you touch, and
[`[TestExport]`](/guide/testing#when-you-want-a-real-object-not-a-mock) is the better tool. When the
parameter is data rather than a service,
[`[InjectValues]`](/guide/testing#when-the-parameter-is-not-a-service-at-all) is.

## Next

- [Testing modules](/guide/testing) — the parts shared by both test frameworks
- [Testing registrations](/guide/testing-registrations) — asserting on what a module registered
