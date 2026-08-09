# Mocks and values

Not every parameter should come from the real container. Two attributes cover the rest, and a third
lets a single test override a registration.

## Mocking

`[Mock]` on a parameter substitutes that service. The substitute is **registered in the container**,
so anything resolved afterwards depends on the mock rather than the real implementation.

```csharp
[assembly: NSubstituteSupport]
```

```csharp
[ModuleTest]
[ApplicationModule]
public void SendsToTheCustomerAddress(OrderService service, [Mock] IEmailSender sender) {
    service.Place(new Order { Email = "a@b.com" });

    sender.Received().Send("a@b.com");
}
```

`OrderService` is constructed by the container and receives the same substitute the test holds. You
do not wire anything together.

`[NSubstituteSupport]` supplies the mocking library. Without it `[Mock]` fails with a message saying
so. Like the module attributes it applies at assembly, class or method level; assembly is usually
right.

## Supplying values

Some parameters are not services at all. `[InjectValues]` provides them, and it mixes with
resolution: a record can take both a service and a literal.

```csharp
public record InjectModel(IDependencyOne DependencyOne, string StringValue);

[ModuleTest]
[SutModule]
public void InjectTestValue([InjectValues("Hello World!")] InjectModel model) {
    Assert.NotNull(model.DependencyOne);                // from the container
    Assert.Equal("Hello World!", model.StringValue);    // from the attribute
}
```

The values are matched to the constructor parameters the container cannot supply, so you only list
the ones it could not work out for itself.

## Registering something for one test

`[TestExport]` adds a registration to the test's container without touching the module. Useful for a
stub you want the real container to construct, or for overriding one service in one place.

```csharp
[ModuleTest]
[ApplicationModule]
[TestExport(typeof(IClock), Implementation = typeof(FixedClock), Lifetime = ServiceLifetime.Singleton)]
public void UsesTheFixedClock(IOrderService service) { }
```

| Property | |
|---|---|
| *(constructor)* | the service type |
| `Implementation` | defaults to the service type when omitted |
| `Lifetime` | defaults to `Transient` |

It applies at assembly, class or method level, so a stub every test needs can sit at the top of the
file once.

## Which one to reach for

| | |
|---|---|
| `[Mock]` | you want to assert on the interaction — what was called, with what |
| `[TestExport]` | you want a real object with different behaviour, constructed by the container |
| `[InjectValues]` | the parameter is data, not a service |

## A trap worth knowing

An [intercepted](/guide/interception) service resolves as a **generated wrapper**, so this fails:

```csharp
Assert.IsType<Orders>(provider.GetRequiredService<IOrders>());   // it is Orders_Intercepted
```

Assert on the interface or on behaviour instead. The same applies to a
[decorated](/guide/decorators) service, where the outermost decorator is what resolves.
