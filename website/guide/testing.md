# Testing modules

## The problem

Here is a service with two dependencies, one of which has a dependency of its own:

```csharp
[SingletonService]
public class Weather(ISummaryProvider summaryProvider, ITemperatureProvider temperatureProvider) {
    public IEnumerable<WeatherForecast> GetWeatherForecast() { /* … */ }
}
```

To test it, you have two options and neither is good.

**Construct it by hand.** You end up rebuilding the object graph in the test:

```csharp
[Fact]
public void GetForecast() {
    var weather = new Weather(
        new SummaryProvider(new AiSummaryProvider()),
        new TemperatureProvider());
    // …
}
```

Every constructor change breaks every test that touches the type, and the wiring you are testing is
the wiring you just wrote — not the wiring your application actually uses.

**Build a provider in each test.** Correct, but it is four lines of ceremony before you get to the
part you care about, repeated in every test, and now you have a provider to dispose:

```csharp
[Fact]
public void GetForecast() {
    var services = new ServiceCollection();
    services.AddModule<ApplicationModule>();
    using var provider = services.BuildServiceProvider();

    var weather = provider.GetRequiredService<Weather>();
    // …
}
```

## How DependencyModules helps

`DependencyModules.xUnit` does the second thing for you. You say which modules to load, and the
services your test needs arrive as **method parameters**, resolved from a provider built out of your
real modules:

```shell
dotnet add package DependencyModules.xUnit
dotnet add package DependencyModules.NSubstitute
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

Three things are happening in that test:

- **`[ModuleTest]`** replaces `[Fact]`. It builds a service provider and runs your method against it.
- **`[ApplicationModule]`** says which modules to load. It is the attribute the generator produced
  for your module — see [composing modules](/guide/modules#composing-modules).
- **`Weather weather`** is resolved from the resulting provider, along with its whole dependency
  graph.

Change `Weather`'s constructor and the test keeps compiling, because the test never mentioned the
constructor.

## Stop repeating the module list

Module attributes apply at **assembly, class or method level**, and they accumulate. Put the ones
every test needs in one file at the assembly level:

```csharp
// Bootstrap.cs
using DependencyModules.NSubstitute;

[assembly: ApplicationModule]
[assembly: NSubstituteSupport]
```

Every test in the project now gets `ApplicationModule` without saying so:

```csharp
public class WeatherTests {
    [ModuleTest]
    public void UsesTheAssemblyModules(Weather weather) { }

    [ModuleTest]
    [DiagnosticsModule]                 // this test gets DiagnosticsModule as well
    public void AddsOneMore(Weather weather, IProfiler profiler) { }
}
```

## Data-driven tests

`[ModuleTest]` composes with xUnit's data attributes. Data parameters come first, injected ones
after:

```csharp
[ModuleTest]
[InlineData("one")]
[InlineData("two")]
public void MultipleRows(string value, ITemperatureProvider provider) {
    Assert.NotNull(value);       // from [InlineData]
    Assert.NotNull(provider);    // from the container
}
```

## Scopes and isolation

Each `[ModuleTest]` gets **its own provider**, so a singleton mutated in one test cannot leak into
another. Within a test, ask for `IServiceProvider` and create scopes as usual:

```csharp
[ModuleTest]
public void ScopedServicesAreScoped(IServiceProvider provider) {
    using var first = provider.CreateScope();
    using var second = provider.CreateScope();

    var one = first.ServiceProvider.GetRequiredService<IUnitOfWork>();

    Assert.Same(one, first.ServiceProvider.GetRequiredService<IUnitOfWork>());
    Assert.NotSame(one, second.ServiceProvider.GetRequiredService<IUnitOfWork>());
}
```

## What is worth testing

Asserting that `[SingletonService]` produced an `AddSingleton` call is testing this library, and this
library has its own tests. Spend your assertions on the things the compiler cannot check:

- a [convention](/guide/conventions) matched the types you meant — and, more usefully, did **not**
  match the ones you did not
- a [conditional registration](/guide/environments) picks the right implementation per environment
- [decorators](/guide/decorators) nest in the order you intended
- a service resolves at all, which catches a missing registration in a module you compose

The build already covers a good deal of the rest. A convention that matches nothing is
[DM0005](/reference/diagnostics#dm0005), and a service that cannot be constructed is
[DM0002](/reference/diagnostics#dm0002) — both before a test runs.

## Next

- [Mocks and values](/guide/testing-mocks) — faking one service while the rest stays real
- [Testing registrations](/guide/testing-registrations) — asserting on what a module registered
