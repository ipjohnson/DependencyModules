# Testing modules

Testing a module means building a provider from it and asking what came out. The xUnit package does
that for you: a test names the modules it wants, declares the services it needs as parameters, and
gets them injected.

```shell
dotnet add package DependencyModules.xUnit
dotnet add package DependencyModules.xUnit.NSubstitute
```

## A first test

```csharp
using DependencyModules.xUnit.Attributes;

public class OrderServiceTests {
    [ModuleTest]
    [ApplicationModule]
    public void PlacingAnOrderSendsConfirmation(OrderService service) {
        service.Place(new Order());
    }
}
```

`[ModuleTest]` replaces `[Fact]`. The module attribute says which modules to load — it is the
attribute the generator produced for `ApplicationModule`. Every parameter is resolved from the
provider that results.

## Where module attributes go

They apply at assembly, class or method level, and they accumulate. Put the ones every test needs at
the assembly level and stop repeating them:

```csharp
[assembly: ApplicationModule]
[assembly: NSubstituteSupport]
```

```csharp
public class OrderServiceTests {
    [ModuleTest]
    public void UsesTheAssemblyModules(OrderService service) { }

    [ModuleTest]
    [DiagnosticsModule]                 // this test also gets DiagnosticsModule
    public void AddsOneMore(OrderService service, IProfiler profiler) { }
}
```

## Data-driven tests

`[ModuleTest]` composes with xUnit's data attributes. Data parameters come first, injected ones
after:

```csharp
[ModuleTest]
[InlineData("one")]
[InlineData("two")]
[SutModule]
public void MultipleRows(string value, IDependencyOne one) {
    Assert.NotNull(value);
    Assert.NotNull(one);
}
```

## Scopes

A `[ModuleTest]` gets its own provider, so singletons do not leak between tests. Within one test you
can create scopes as usual:

```csharp
[ModuleTest]
[ApplicationModule]
public void ScopedServicesAreScoped(IServiceProvider provider) {
    using var first = provider.CreateScope();
    using var second = provider.CreateScope();

    var one = first.ServiceProvider.GetRequiredService<IUnitOfWork>();

    Assert.Same(one, first.ServiceProvider.GetRequiredService<IUnitOfWork>());
    Assert.NotSame(one, second.ServiceProvider.GetRequiredService<IUnitOfWork>());
}
```

## What to test, and what not to

Testing that `[SingletonService]` produced `AddSingleton` is testing this library. Assert instead on
the things the compiler cannot check for you:

- a convention matched the set of types you meant — and, more usefully, did **not** match the ones
  you did not
- a conditional registration selects the right implementation in each environment
- decorators nest in the order you intended
- a service resolves at all, which catches a missing registration in a module you compose

The build covers the rest — a convention that matches nothing is
[DM0005](/reference/diagnostics#dm0005) and a service that cannot be constructed is
[DM0002](/reference/diagnostics#dm0002), both before a test runs.

## Next

- [Mocks and values](/guide/testing-mocks) — substituting services and supplying literals
- [Testing registrations](/guide/testing-registrations) — asserting on what a module registered
