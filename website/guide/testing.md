# Testing

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

## Mocking

`[Mock]` on a parameter substitutes that service. The substitute is **registered in the container**,
so anything resolved afterwards depends on the mock rather than the real implementation — which is
the point.

```csharp
[assembly: NSubstituteSupport]
```

```csharp
[ModuleTest]
public void SendsToTheCustomerAddress(OrderService service, [Mock] IEmailSender sender) {
    service.Place(new Order { Email = "a@b.com" });

    sender.Received().Send("a@b.com");
}
```

`OrderService` is constructed by the container and receives the same substitute the test holds.

`[NSubstituteSupport]` is what supplies the mocking library. Without it, `[Mock]` has nothing to
create substitutes with and the test fails with a message saying so.

## Supplying values

Some parameters are not services. `[InjectValues]` provides them, and mixes with resolution — a
record can take both a service and a literal:

```csharp
public record InjectModel(IDependencyOne DependencyOne, string StringValue);

[ModuleTest]
[SutModule]
public void InjectTestValue([InjectValues("Hello World!")] InjectModel model) {
    Assert.NotNull(model.DependencyOne);          // from the container
    Assert.Equal("Hello World!", model.StringValue);   // from the attribute
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

## Registering something for one test

`[TestExport]` adds a registration to the test's container without touching the module. Useful for a
stub you want the real container to construct, or for overriding one service.

```csharp
[ModuleTest]
[ApplicationModule]
[TestExport(typeof(IClock), Implementation = typeof(FixedClock), Lifetime = ServiceLifetime.Singleton)]
public void UsesTheFixedClock(IOrderService service) { }
```

It applies at assembly, class or method level like the module attributes, and `Implementation`
defaults to the service type when omitted.

## Testing what a module registered

The xUnit package is for testing *behaviour through* the container. For assertions about the
registrations themselves — lifetimes, keys, how many things matched — build the collection directly
and look at it. No test framework integration needed:

```csharp
using DependencyModules.Runtime;

var services = new ServiceCollection();

services.AddModules(new DataModule());

var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IRepository));

Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
Assert.Equal(typeof(SqlRepository), descriptor.ImplementationType);
```

This is the right shape for testing a [convention](/guide/conventions), because the interesting
question is usually *which types matched* rather than what one of them does:

```csharp
var registered = services
    .Where(d => d.ServiceType == typeof(IRepository))
    .Select(d => d.ImplementationType!.Name)
    .OrderBy(name => name)
    .ToArray();

Assert.Equal(["OrderRepository", "ProductRepository"], registered);
```

::: warning One provider per assertion set
Building a provider twice gives you two sets of singletons. If a test resolves a service from one
provider and asserts on a singleton it captured from another, it will compare two different
instances and the failure will look like the registration is wrong.

```csharp
var provider = services.BuildServiceProvider();   // build once

var service = provider.GetRequiredService<IOrderService>();
var log = provider.GetRequiredService<Log>();     // same provider, same singleton
```
:::

## Testing conditional registrations

Registrations gated on the [environment](/guide/environments) are decided when the modules are
applied, so the environment has to be supplied then:

```csharp
var services = new ServiceCollection();

services.AddModules(new ModuleEnvironment("Development"), new ApplicationModule());

Assert.IsType<FakeEmailSender>(
    services.BuildServiceProvider().GetRequiredService<IEmailSender>());
```

Supply nothing and the process environment is used, which defaults to `"Production"` — so a test for
a development-only service **must** name the environment or it will silently test the wrong branch.

A theory covers both sides in one place:

```csharp
[Theory]
[InlineData("Development", typeof(FakeEmailSender))]
[InlineData("Production", typeof(SmtpEmailSender))]
public void SelectsBySender(string environment, Type expected) {
    var services = new ServiceCollection();

    services.AddModules(new ModuleEnvironment(environment), new ApplicationModule());

    Assert.IsType(expected, services.BuildServiceProvider().GetRequiredService<IEmailSender>());
}
```

## Testing decorators and interceptors

Both change what resolving gives you, so assert on the resolved type and on the effect:

```csharp
var provider = services.BuildServiceProvider();

var repository = provider.GetRequiredService<IRepository>();

Assert.IsType<CachingRepository>(repository);      // the decorator is on the outside
```

For [ordering](/guide/decorators#ordering), have each decorator contribute to a string and assert the
nesting, which is far clearer than reflecting over the chain:

```csharp
Assert.Equal("outer(inner(core))", provider.GetRequiredService<IOrdered>().Describe());
```

An [interceptor](/guide/interception) is easiest to test through something it records:

```csharp
var provider = services.BuildServiceProvider();
var log = provider.GetRequiredService<InterceptLog>();

provider.GetRequiredService<IOrders>().Count("acme");

Assert.Equal(["intercepted Count"], log.Lines);
```

Note that a service resolves as a **generated wrapper**, so `Assert.IsType<Orders>(…)` fails where
you might expect it to pass. Assert on behaviour, or on the interface.

## Testing scopes

```csharp
using var first = provider.CreateScope();
using var second = provider.CreateScope();

var one = first.ServiceProvider.GetRequiredService<IUnitOfWork>();

Assert.Same(one, first.ServiceProvider.GetRequiredService<IUnitOfWork>());
Assert.NotSame(one, second.ServiceProvider.GetRequiredService<IUnitOfWork>());
```

## What to test, and what not to

Testing that `[SingletonService]` produced `AddSingleton` is testing this library, not your code. The
registrations worth asserting on are the ones where **you** made a decision the compiler cannot
check:

- a convention matched the set of types you meant — and, more usefully, did **not** match the ones
  you did not
- a conditional registration selects the right implementation in each environment
- decorators nest in the order you intended
- a service resolves at all, which catches a missing registration in a module you compose

The build already tells you about the rest. A convention that matches nothing is
[DM0005](/reference/diagnostics#dm0005), a service that cannot be constructed is
[DM0002](/reference/diagnostics#dm0002), and both fail before a test ever runs.
