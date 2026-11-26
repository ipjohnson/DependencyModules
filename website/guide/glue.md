# Collections and Func parameters

The generator can write the code that builds an object, when the object does not need a registration. This page calls that code the glue. The glue has two parts:

- A class can be an item of a map, a set, or a list. The items are not services.
- A class can take a `Func` constructor parameter. The generator registers the `Func`, and the `Func` builds an object.

Use the glue for a class that the application does not replace, and that you do not get from the service provider by its type.

## A map of items

`[IntoMap<TKey, TValue>(key)]` adds a class to the map of `TKey` to `TValue`. A class gets the map as `IReadOnlyDictionary<TKey, TValue>`.

```csharp
using DependencyModules.Runtime.Attributes;

namespace Shop;

public interface IRequestHandler
{
    string Handle(string request);
}

[IntoMap<string, IRequestHandler>("orders/get")]
public class GetOrderHandler(IClock clock) : IRequestHandler
{
    public string Handle(string request) => "order";
}

[IntoMap<string, IRequestHandler>("orders/list")]
public class ListOrdersHandler : IRequestHandler
{
    public string Handle(string request) => "orders";
}

[SingletonService]
public class RequestRouter(IReadOnlyDictionary<string, IRequestHandler> handlers)
{
    public IEnumerable<string> Routes => handlers.Keys;

    public string Route(string name, string request) => handlers[name].Handle(request);
}

[DependencyModule]
public partial class ShopModule;
```

The map builds `GetOrderHandler` when the application first reads the key `"orders/get"`. The map gets `IClock` from the service provider. The map then keeps the handler.

`Keys`, `Count`, and `ContainsKey` build no item. `Values` and the enumerator build each item that they give.

The key can be a string, a number, an enum value, or a `Type`. `Keys` gives the keys in their sort sequence. For string keys, the sequence is ordinal.

Two items of a map cannot have the same key. If the two classes are in one module, the generator gives the error DM0028. If the two classes are in different modules, `AddModule` gives an `InvalidOperationException`. The message contains the two classes.

## A set of items

`[IntoSet<T>]` adds a class to the set of `T`. A class gets the set as `IReadOnlySet<T>`.

```csharp
using DependencyModules.Runtime.Attributes;

namespace Shop;

public interface IHealthCheck
{
    bool IsHealthy();
}

[IntoSet<IHealthCheck>]
public class DatabaseCheck(IOrderStore store) : IHealthCheck
{
    public bool IsHealthy() => true;
}

[IntoSet<IHealthCheck>]
public class QueueCheck : IHealthCheck
{
    public bool IsHealthy() => true;
}

[SingletonService]
public class HealthReport(IReadOnlySet<IHealthCheck> checks)
{
    public bool IsHealthy() => checks.All(check => check.IsHealthy());
}
```

A set compares its items. Thus the set builds all items when the application first reads it.

## A list of items

A list has a sequence. There are two attributes for a list. All items of a list of `T` must use the same attribute, and the same `TOrder`. A class gets the list as `IReadOnlyList<T>`.

### Sequence from types

`[IntoList<T>(params Type[] dependsOn)]` puts the class after each item that is assignable to a type in `dependsOn`.

```csharp
using DependencyModules.Runtime.Attributes;

namespace Shop;

public interface IStartupStep
{
    void Run();
}

[IntoList<IStartupStep>]
public class MigrateDatabase : IStartupStep
{
    public void Run() { }
}

[IntoList<IStartupStep>(typeof(MigrateDatabase))]
public class LoadCatalog(IOrderStore store) : IStartupStep
{
    public void Run() { }
}

[SingletonService]
public class Startup(IReadOnlyList<IStartupStep> steps)
{
    public void Run()
    {
        foreach (var step in steps)
        {
            step.Run();
        }
    }
}
```

A type in `dependsOn` can be a base class or an interface. The class is then after all items that have that base class or interface. A type with no item in the list has no effect.

### Sequence from values

`[IntoList<T, TOrder>(TOrder value, params TOrder[] dependsOn)]` gives the class an order value. The order is then not related to the names of the classes.

```csharp
using DependencyModules.Runtime.Attributes;

namespace Shop;

public enum Stage
{
    Security,
    Routing,
    Endpoint,
}

public interface IMiddleware;

[IntoList<IMiddleware, Stage>(Stage.Security)]
public class Authentication : IMiddleware;

[IntoList<IMiddleware, Stage>(Stage.Security)]
public class RateLimit : IMiddleware;

[IntoList<IMiddleware, Stage>(Stage.Routing)]
public class Router : IMiddleware;
```

More than one item can have the same value. These items are one level of the list. The list sorts the items by `value`.

`dependsOn` puts the class after each item that has one of the values. This is applicable when the values have no sort sequence that is correct for you, for example strings:

```csharp
[IntoList<IMiddleware, string>("routing", "security")]
public class Router : IMiddleware;
```

A value with no item in the list has no effect.

### Items with no sequence between them

If the attributes give no sequence between two items, the list sorts them by `value`. If the values are equal, or the list has no values, the list sorts them by the full name of the class. Thus the sequence does not change when the load sequence of the modules changes.

If the items depend on each other in a cycle, `AddModule` gives an `InvalidOperationException`. The message contains the classes of the cycle.

## Items are not services

The service provider has no registration for an item. In the examples, `GetServices<IRequestHandler>()` gives no handler, and `GetService<GetOrderHandler>()` gives `null`. Thus decorators, interceptors, `Replace` registrations, and `[Mock]` have no effect on an item. They have an effect on the services that the item gets from the service provider.

A collection always builds its items. If an item class also has a service attribute, the collection does not use that registration.

## Lifetime

When the modules load, the glue builds no object. The registration of each collection is transient. Thus each class that takes a collection gets its own collection, and the collection has the lifetime of that class. A collection builds an item when the application first reads the item, and it keeps the item.

To build the items one time for the application, take the collection in a singleton.

A collection disposes the items that it built. The service provider disposes the collection with the scope that made it.

## Modules and collections

The items are part of a module, as registrations are. A module adds its items when it loads. When more than one module loads, a collection contains the items of all of them. The modules can be in different projects.

Each `Into*` attribute has a `Realm` property. It has the same function as on the service attributes. For more information, refer to [Realms](./modules.md#realms).

An [environment attribute](./environments.md) on an item class is applicable to all `Into*` attributes of the class.

If a module or the application registers `IReadOnlyDictionary<TKey, TValue>`, `IReadOnlySet<T>`, or `IReadOnlyList<T>` for the same types, the glue does not replace that registration.

## Func parameters

When a service has a `Func` constructor parameter, the generator can register the `Func`.

```csharp
using DependencyModules.Runtime.Attributes;

namespace Shop;

public readonly record struct OrderId(int Value);

public class OrderProcessor(OrderId id, IOrderStore store)
{
    public void Process() => store.Save(id.Value.ToString());
}

[SingletonService]
public class Checkout(Func<OrderId, OrderProcessor> createProcessor)
{
    public void Complete(OrderId id) => createProcessor(id).Process();
}
```

`OrderProcessor` has no service attribute and no registration. The `Func` builds a new `OrderProcessor` for each call. It gives `id` to the constructor and gets `IOrderStore` from the service provider.

The last type argument of the `Func` is the type that it gives. The other type arguments are the caller arguments. The rules are as follows:

| `Func` | Registration |
| --- | --- |
| `Func<A, T>`, and `T` is a concrete class | The `Func` builds `T` with `new`. Each caller argument goes to the constructor parameter of `T` that has the same type. |
| `Func<T>`, and `T` is a concrete class with no service attribute | The `Func` gets `T` from the service provider. If the service provider has no registration, the `Func` builds `T` with `new`. |
| `Func<T>`, and `T` is an interface, an abstract class, or a class with a service attribute | The `Func` gets `T` from the service provider. |
| All other `Func` types | None. |

The generator makes no registration in these conditions:

- `Func<T>` gives a type from a `System` namespace, for example `Func<Task>`. Such a delegate is usually a callback.
- `Func<A, T>` gives an interface, a value type, or a type from a `System` or `Microsoft` namespace.

If the caller arguments do not match a constructor of the class, the generator gives the warning DM0030 and makes no registration. Two caller arguments cannot have the same type.

The generator reads the `Func` parameters of these members:

- The constructors of a class with a service attribute.
- The parameters of a static method with a service attribute.
- The constructor of a collection item.
- The constructor of each object that the glue builds.

The generator does not read the classes that only a convention registers.

The registration of a `Func` is transient. The object that a `Func` builds has no owner in the service provider. If the object is disposable, the code that calls the `Func` must dispose it.

The glue registers the `Func` types and the collections after all other registrations of the modules, and it uses `TryAdd`. Thus, if a module or the application registers the same `Func` type, the glue does not replace that registration.

## Constructor parameters

When the glue builds an object, it gets each constructor parameter from the first applicable rule:

1. A caller argument of the `Func`, if the parameter has the same type.
2. An `IServiceProvider` parameter gets the service provider. A parameter with `[FromKeyedServices]` gets the keyed service.
3. A parameter that is a concrete class with no service attribute. The glue gets it from the service provider. If the service provider has no registration, the glue builds it with `new`, with these same rules.
4. All other parameters come from the service provider. A nullable parameter is `null` if there is no registration.

Rule 3 lets the glue build a class that has no registration. It also keeps a registration that the application makes without a service attribute, for example with `AddDbContext` or `AddSingleton(instance)`.

Rule 3 is not applicable to these types. They come from the service provider:

- A type from a `System` or `Microsoft` namespace.
- A class that has a constructor parameter with a value type or the `string` type.
- A class that the generated code cannot build, for example a class with no `public` or `internal` constructor.

The glue selects the constructor in this sequence:

1. A constructor with `[ActivatorUtilitiesConstructor]`.
2. For a `Func` with caller arguments, a constructor that has a parameter for each caller argument.
3. The constructor with the most parameters.

## Classes that the glue cannot build

If the glue cannot build a class with an `Into*` attribute, the generator gives the warning DM0027. The class is not in the collection. The message gives the cause, for example:

- The class is abstract, static, or an open generic class.
- The class is not assignable to the item type of the collection.
- The class has no constructor that the generated code can call.
- A constructor parameter has a value type or the `string` type.
