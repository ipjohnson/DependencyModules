# Decorators

A decorator is a wrapper class for a service. The decorator implements the service type and gets the service in its constructor. When you get the service type from the service provider, you get the decorator. The decorator then calls the service.

## Declare a decorator

Put `[Decorator]` on the class:

```csharp
using DependencyModules.Runtime.Attributes;

namespace Orders;

public interface IOrderService
{
    string Place(string item);
}

[SingletonService]
public class OrderService : IOrderService
{
    public string Place(string item) => $"placed {item}";
}

public interface IAuditLog
{
    void Write(string line);
}

[SingletonService]
public class ConsoleAuditLog : IAuditLog
{
    public void Write(string line) => Console.WriteLine(line);
}

[Decorator]
public class AuditedOrderService(IOrderService inner, IAuditLog log) : IOrderService
{
    public string Place(string item)
    {
        log.Write($"order for {item}");
        return inner.Place(item);
    }
}
```

When you get `IOrderService`, the service provider gives `AuditedOrderService`. `AuditedOrderService` gets `OrderService` in the `inner` parameter.

The generator finds the service type from the constructor. The service type is the first constructor parameter with a type that the class declaration of the decorator contains. The generator does not examine the interfaces of base classes or the interfaces that an interface derives from. If the generator finds no service type, it ignores the decorator and gives no diagnostic. The `Service` property can also set the service type, for example `[Decorator(Service = typeof(IOrderService))]`.

The generator does not register the decorator class as a service. The service provider gives the other constructor parameters, for example `IAuditLog` in the example.

Give the decorator a `public` constructor. The generated code calls this constructor.

## Which registrations a decorator changes

The decorators change the registrations after all modules of the load operation add their services. A decorator changes each registration of its service type in the service collection. All loaded modules can add these registrations. Your code can also add them before the call to `AddModules`.

A decorator changes each registration only one time. When two modules contain the same decorator, the decorator also changes each registration only one time.

The decorated registration keeps its lifetime. A decorator can change keyed registrations, instance registrations, and factory registrations. A keyed registration keeps its key.

A decorator does not change registrations that you add after the modules load.

For a registration with an implementation type, the decorator moves the registration to a private service key. The decorator then gets the service with `GetRequiredKeyedService`. Thus the service provider must support keyed services. The service provider continues to make and dispose the decorated service.

## Decorator sequence

The `Order` property sets the sequence of decorators for one service type. If the `Order` value of decorator A is less than the `Order` value of decorator B, B is the outer decorator. Thus B gets the call before A.

```csharp
using DependencyModules.Runtime.Attributes;

namespace Orders;

public interface IPriceService
{
    string Describe();
}

[SingletonService]
public class PriceService : IPriceService
{
    public string Describe() => "price";
}

[Decorator(Order = 10)]
public class InnerPriceDecorator(IPriceService inner) : IPriceService
{
    public string Describe() => $"inner({inner.Describe()})";
}

[Decorator(Order = 20)]
public class OuterPriceDecorator(IPriceService inner) : IPriceService
{
    public string Describe() => $"outer({inner.Describe()})";
}
```

In this example, `Describe()` gives `outer(inner(price))`.

The decorators of all modules and the interceptors are in one sequence. The default `Order` value is 0. The source code recommends values from 0 to 999 for packages and values of 1000 and more for application code. Then the decorators of the application are the outer decorators. The generator does not examine these ranges.

Write `Order` as a number, for example `Order = 1000`. The generator reads the text of the value. If you use a constant or `1_000`, the `Order` value is 0, and the generator gives no diagnostic. `[Decorate]` also accepts a constant.

If two decorators in one module have the same service type and the same `Order` value, the generator gives the error DM0007.

## Generic decorators

A generic decorator decorates a generic service type:

```csharp
using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Conventions;

namespace Orders.Handlers;

public interface IHandler<TRequest, TResponse>
{
    TResponse Handle(TRequest request);
}

public record CreateOrder(string Item);

public record CancelOrder(string OrderId);

public class CreateOrderHandler : IHandler<CreateOrder, string>
{
    public string Handle(CreateOrder request) => "created";
}

public class CancelOrderHandler : IHandler<CancelOrder, string>
{
    public string Handle(CancelOrder request) => "cancelled";
}

[Decorator]
public class LoggingHandler<TRequest, TResponse>(IHandler<TRequest, TResponse> inner)
    : IHandler<TRequest, TResponse>
{
    public TResponse Handle(TRequest request)
    {
        Console.WriteLine($"handle {typeof(TRequest).Name}");
        return inner.Handle(request);
    }
}

[DependencyModule]
public partial class HandlerModule : IConventionModule
{
    public void Conventions(IConventionDefinitions conventions)
    {
        conventions.RegisterAll(typeof(IHandler<,>)).AsScoped();
    }
}
```

The generator makes a closed decorator for each closed registration of the service type in the project. In this example, `LoggingHandler` decorates `IHandler<CreateOrder, string>` and `IHandler<CancelOrder, string>`. The registrations can be from service attributes or from conventions.

These conditions are applicable to a generic decorator:

- The decorator must have the same type parameters as the service type, in the same sequence.
- The generator uses only the closed service types that the same project registers.
- If a closed service type does not agree with the constraints of the decorator, the generator does not use the decorator for that type.

The generator cannot decorate an open generic registration, for example a registration of `IRepository<>` to `Repository<>`. If the project has only an open generic registration of the service type, the generator gives the warning DM0013. If the project also contains closed registrations, the generator decorates only the closed registrations. It gives no diagnostic for the open generic registration.

To decorate each closed type, register closed types. For example, declare classes that are not generic, such as `OrderRepository : IRepository<Order>`. A convention registers such a class as each closed type that it implements.

## Decorate from a module

When you cannot put `[Decorator]` on the decorator class, use `[Decorate]` on a module. For example, the decorator can be in a package.

```csharp
using DependencyModules.Runtime.Attributes;

namespace Orders;

public class TimedOrderService(IOrderService inner) : IOrderService
{
    public string Place(string item) => inner.Place(item);
}

[DependencyModule]
[Decorate(typeof(IOrderService), typeof(TimedOrderService), Order = 30)]
public partial class OrdersModule;
```

The first argument is the service type. The next argument is the decorator type. The decorator must have a `public` constructor with a parameter of the service type. If it does not have such a constructor, the generator ignores the decorator and gives no diagnostic. The [generator log](./troubleshooting.md#write-a-generator-log) shows the cause. Only the module that has the attribute uses this decorator.

If the service type is an open generic type, the decorator type must also be an open generic type. If the decorator type is not generic, the generator gives the warning DM0013.

## Decorate one implementation

When a service type has more than one implementation, set `Implementation` to decorate only one of them:

```csharp
using DependencyModules.Runtime.Attributes;

namespace Orders.Payments;

public interface IPaymentMethod
{
    string Pay(decimal amount);
}

[SingletonService]
public class CardPayment : IPaymentMethod
{
    public string Pay(decimal amount) => "card";
}

[SingletonService]
public class InvoicePayment : IPaymentMethod
{
    public string Pay(decimal amount) => "invoice";
}

[Decorator(Implementation = typeof(CardPayment))]
public class CardPaymentCheck(IPaymentMethod inner) : IPaymentMethod
{
    public string Pay(decimal amount) => amount > 0 ? inner.Pay(amount) : "rejected";
}
```

`CardPaymentCheck` decorates only the registration of `CardPayment`.

`Implementation` has an effect only on type registrations. In an instance registration or a factory registration, the decorator does not know the implementation type. Thus the decorator changes the registration.

If a module writes [generated factories](./aot.md#generated-factories), its registrations are factory registrations. The generator then gives the warning DM0022, because the decorator changes all registrations of the service type. If `[DependencyModule]` sets `GenerateFactories`, the generator uses this value for the module. It does not use the `DependencyModules_GenerateFactories` MSBuild property.

## Environment conditions

A `[Decorator]` class can have environment attributes, for example `[IfEnvironment("Development")]`. The decorator then changes the registrations only when the conditions are true. If the conditions are false, the other decorators keep their positions in the sequence. For the attributes, refer to [Environments](./environments.md).

The generator ignores the environment attributes of a decorator that `[Decorate]` adds.

## Realms

A decorator without `Realm` is applicable in each module that is not realm-only. A decorator with `Realm` is applicable only in the module that `Realm` identifies. For realms, refer to [Realms](./modules.md#realms).

## Decorators in code

To decorate registrations with your code, implement `IServiceCollectionConfiguration.ConfigureDecorators` on a module. This method runs after all generated decorators. For more information, refer to [Registration code in the module](./modules.md#registration-code-in-the-module).
