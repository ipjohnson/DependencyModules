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
var weather = new Weather(
    new SummaryProvider(new AiSummaryProvider()),
    new TemperatureProvider());
```

Every constructor change breaks every test that touches the type, and the wiring you are testing is
the wiring you just wrote — not the wiring your application actually uses.

**Build a provider in each test.** Correct, but it is four lines of ceremony before you get to the
part you care about, repeated in every test, and now you have a provider to dispose:

```csharp
var services = new ServiceCollection();
services.AddModule<ApplicationModule>();
using var provider = services.BuildServiceProvider();

var weather = provider.GetRequiredService<Weather>();
```

## How DependencyModules helps

A test framework integration does the second thing for you. You say which modules to load, and the
services your test needs arrive as **method parameters**, resolved from a provider built out of your
real modules:

```csharp
public class WeatherTests {
    [ModuleTest]
    [ApplicationModule]
    public void GetForecast(Weather weather) {
        var forecast = weather.GetWeatherForecast().ToArray();

        // assert on forecast
    }
}
```

Three things are happening in that test:

- **`[ModuleTest]`** replaces your framework's test attribute. It builds a service provider and runs
  your method against it.
- **`[ApplicationModule]`** says which modules to load. It is the attribute the generator produced
  for your module — see [composing modules](/guide/modules#composing-modules).
- **`Weather weather`** is resolved from the resulting provider, along with its whole dependency
  graph.

Change `Weather`'s constructor and the test keeps compiling, because the test never mentioned the
constructor.

## Pick an integration

One package per test framework. Install the one matching the framework you already use:

| Package | Framework | |
|---|---|---|
| `DependencyModules.xUnit` | xUnit v3 | [xUnit](/guide/testing-xunit) |
| `DependencyModules.NUnit` | NUnit | [NUnit](/guide/testing-nunit) |

```shell
dotnet add package DependencyModules.xUnit
```

This page is the part they share, and it is most of it. The two framework pages cover only what
differs — how data rows are supplied, and what each framework's own attributes do around a module
test.

::: warning Reference one integration, not both
Each defines a `ModuleTestAttribute`. They share a name and nothing else, because each has to derive
from what its own framework requires. A project referencing both would need to disambiguate every
`[ModuleTest]`, which is not a configuration worth having.
:::

Everything else — `[Mock]`, `[TestExport]`, `[InjectValues]`, keyed services — lives in
`DependencyModules.Testing`, which your integration brings in. Those types name no test framework, so
both integrations hand you the *same* attribute rather than a copy of it. They need a
`using DependencyModules.Testing.Attributes;` alongside the one for `[ModuleTest]`.

## Stop repeating the module list

Module attributes apply at **assembly, class or method level**, and they accumulate. Put the ones
every test needs in one file at the assembly level:

```csharp
// Bootstrap.cs
using DependencyModules.NSubstitute;
using MyApp.Tests;                  // the namespace the module is declared in

[assembly: ApplicationModule]
[assembly: NSubstituteSupport]      // or [MoqSupport] / [FakeItEasySupport]
```

That second `using` is easy to miss. A module generates its attribute in the module's own
namespace, and an assembly-level attribute has no namespace context to inherit — so without it the
build fails with `CS0246: The type or namespace name 'ApplicationModuleAttribute' could not be
found`, naming a type you never wrote. Importing the namespace or writing the attribute qualified,
`[assembly: MyApp.Tests.ApplicationModule]`, both work.
[DM0016](/reference/diagnostics#dm0016) reports it and names the namespace to import, for a module
declared here or one from a referenced package.

A test project has no entry point, so [DM0019](/reference/diagnostics#dm0019) — which reports an
assembly-level module attribute in the wrong file — stays quiet here. That is deliberate: assembly
attributes are read at run time by the test integration, and a file of their own is exactly where
they belong.

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

`NSubstituteSupport` is what enables [`[Mock]`](/guide/testing-mocking), and it comes from a separate
package — one per mocking library, so use whichever you already have. See
[Mocking frameworks](/guide/testing-mocking).

## A container per test

Each test gets **its own provider**, built before the test runs and disposed after it, so a singleton
mutated in one test cannot leak into another. That holds per *iteration*, not merely per method — a
data row, a repeat and a retry each get a fresh container.

Within a test, ask for `IServiceProvider` and create scopes as usual:

```csharp
[ModuleTest]
public void ScopedServicesAreScoped(IServiceProvider provider) {
    using var first = provider.CreateScope();
    using var second = provider.CreateScope();

    var one = first.ServiceProvider.GetRequiredService<IUnitOfWork>();

    // one is the same instance within first, and a different one in second
}
```

`IServiceProvider` is special-cased: it is the test's container itself, since a container cannot
resolve itself out of itself.

## How a parameter gets filled

Worth knowing when a parameter does not arrive as you expected. Each one is tried in this order, and
the first step that answers wins:

1. **A data row**, if the test has one. Row arguments fill the leading parameters, so anything the
   row supplies is never resolved from the container.
2. **Attributes on the parameter** — `[Mock]`, `[InjectValues]` and anything else implementing
   `ITestParameterValueProvider`. Several may sit on one parameter; one returning nothing stands
   aside for the next.
3. **The container**, honouring `[FromKeyedServices]` when present.
4. **Direct construction.** A concrete type the container does not know is built anyway, through
   `ActivatorUtilities`, with its dependencies resolved from the container.

That last step is why a test can name the class under test directly without registering it:

```csharp
[ModuleTest]
public void ConstructsTheSubjectDirectly(OrderCalculator calculator) { }   // never registered
```

## Keyed services

`[FromKeyedServices]` works on a test parameter the way it does on a constructor parameter:

```csharp
[ModuleTest]
public void ResolvesTheKeyedOne([FromKeyedServices("primary")] IRepository repository) { }
```

See [registering services](/guide/services) for how a registration acquires a key.

## When you want a real object, not a mock

A [mock](/guide/testing-mocking) is right when you intend to **assert on the interaction** — what was
called, with which arguments. When you instead want a working implementation that simply behaves
differently, a mock makes you stub out every member you touch.

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

Like the module attributes it applies at assembly, class or method level, so a stub every test needs
can sit in your bootstrap file once. A `[TestExport]` also beats a mock for the same service,
whatever order the two are declared in — see [ordering](/guide/testing-mocking#what-wins-when-two-things-register-the-same-service).

## When the parameter is not a service at all

Sometimes a test parameter is a type the container cannot build on its own, because part of it is
data rather than a service. `[InjectValues]` supplies the parts the container cannot:

```csharp
public record InjectModel(IDependencyOne DependencyOne, string StringValue);

[ModuleTest]
public void InjectTestValue([InjectValues("Hello World!")] InjectModel model) {
    // model.DependencyOne came from the container
    // model.StringValue came from the attribute
}
```

The values are matched against the constructor parameters the container **cannot** supply, so you
list only what it could not work out for itself.

They are the parameter type's *constructor arguments*, not the parameter's own value — so a
parameter that should simply **be** a value wants a data row instead. `[InlineData]` and NUnit's
`[TestCase]` both compose with `[ModuleTest]`, and the container fills whatever the row does not:

```csharp
[ModuleTest]
[InlineData("978-0132350884")]
[InlineData("978-0201616224")]
public async Task GetBook_FindsEachIsbn(string isbn, IRequestHandler<GetBook, Book?> handler) {
    // isbn came from the row, handler from the container
}
```

Asking for a bare `string` through `[InjectValues]` fails with *"A suitable constructor for type
'System.String' could not be located"*, because that is exactly what it tried to do.

## Choosing between the three

| | Reach for it when |
|---|---|
| [`[Mock]`](/guide/testing-mocking) | you want to assert on the interaction — what was called, with what |
| `[TestExport]` | you want a real object with different behaviour, constructed by the container |
| `[InjectValues]` | the parameter is a type the container cannot finish building, because part of it is data |
| `[InlineData]` / `[TestCase]` | the parameter simply **is** a value — one test per row |

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

## A trap worth knowing about

An [intercepted](/guide/interception) service resolves as a **generated wrapper**, not as your class.
So this fails, confusingly:

```csharp
Assert.IsType<Orders>(provider.GetRequiredService<IOrders>());   // it is Orders_Intercepted
```

Assert on the interface, or on behaviour. The same applies to a [decorated](/guide/decorators)
service, where what resolves is the outermost decorator.

## Next

- [xUnit](/guide/testing-xunit) and [NUnit](/guide/testing-nunit) — what differs per framework
- [Mocking frameworks](/guide/testing-mocking) — faking one service while the rest stays real
- [Testing registrations](/guide/testing-registrations) — asserting on what a module registered
