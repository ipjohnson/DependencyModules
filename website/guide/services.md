# Services

A service attribute tells the generator to register a class or a static factory method. The attributes are in the `DependencyModules.Runtime.Attributes` namespace.

## Lifetimes

Each service attribute sets one lifetime:

| Attribute | Lifetime | Result |
| --- | --- | --- |
| `[SingletonService]` | `Singleton` | The service provider makes one instance and gives it for all requests. |
| `[ScopedService]` | `Scoped` | The service provider makes one instance for each scope. |
| `[TransientService]` | `Transient` | The service provider makes a new instance for each request. |

```csharp
using DependencyModules.Runtime.Attributes;

namespace Shop;

public interface IOrderStore
{
    void Save(string orderId);
}

[ScopedService]
public class OrderStore : IOrderStore
{
    public void Save(string orderId) { }
}
```

## Service type

The service type is the type that you use to get the service from the service provider. The generator selects the service type in this sequence:

1. If the attribute sets `As`, the service type is the `As` type.
2. If the class declaration contains an interface, the service type is the first interface in the declaration.
3. If the class declaration contains no interface, the generator examines the interfaces of the base classes.
4. If the generator finds no interface, the service type is the class.

The generator does not use a capability interface as the service type. These are the capability interfaces:

- `IDisposable` and `IAsyncDisposable`
- `ICloneable`, `IComparable`, `IComparable<T>`, `IEquatable<T>`, and `IConvertible`
- `IFormattable`, `ISpanFormattable`, `IParsable<T>`, and `ISpanParsable<T>`
- `IEnumerable` and `IEnumerable<T>`
- `ISerializable`
- `INotifyPropertyChanged`, `INotifyPropertyChanging`, and `INotifyCollectionChanged`

If you set `As` to a capability interface, the generator uses it. `[CrossWireService]` does not use this list. It registers all interfaces in the declaration.

Each attribute registers one service type. To register a class as two service types, put two attributes on the class:

```csharp
using DependencyModules.Runtime.Attributes;

namespace Shop;

public interface IReadStore
{
    string Read(string key);
}

public interface IWriteStore
{
    void Write(string key, string value);
}

[SingletonService(As = typeof(IReadStore))]
[SingletonService(As = typeof(IWriteStore))]
public class FileStore : IReadStore, IWriteStore
{
    public string Read(string key) => "";

    public void Write(string key, string value) { }
}
```

In this example, the generator writes two registrations. Each registration makes a different instance of `FileStore`. [`[CrossWireService]`](#cross-wired-services) uses one instance for the two service types.

## Keyed services

Set `Key` to register a keyed service. The generator then calls the keyed registration methods, for example `AddKeyedSingleton`.

```csharp
using DependencyModules.Runtime.Attributes;

namespace Shop;

public interface IPaymentGateway
{
    string Name { get; }
}

[SingletonService(Key = "card")]
public class CardGateway : IPaymentGateway
{
    public string Name => "card";
}

[SingletonService(Key = "invoice")]
public class InvoiceGateway : IPaymentGateway
{
    public string Name => "invoice";
}
```

To get a keyed service in a constructor, use `[FromKeyedServices]` from `Microsoft.Extensions.DependencyInjection`:

```csharp
using DependencyModules.Runtime.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Shop;

[TransientService]
public class Checkout([FromKeyedServices("card")] IPaymentGateway gateway)
{
    public string GatewayName => gateway.Name;
}
```

The generator writes the key into the generated code without changes. The key can be a string, a number, a constant, or an enum value.

## Registration type

The `Using` property sets the registration type. The values are in the `RegistrationType` enum:

| Value | Generated call | Result |
| --- | --- | --- |
| `Add` | `AddSingleton`, `AddScoped`, `AddTransient` | Adds a registration. This is the default. |
| `Try` | `TryAddSingleton`, `TryAddScoped`, `TryAddTransient` | Adds the registration only if the service type has no registration. |
| `TryEnumerable` | `TryAddEnumerable` | Adds the registration only if no registration has the same service type and implementation type. |
| `Replace` | `Replace` | The call removes the first registration of the service type. Then it adds this registration. |

```csharp
using DependencyModules.Runtime.Attributes;

namespace Shop;

public interface IClock
{
    DateTimeOffset Now { get; }
}

[SingletonService(Using = RegistrationType.Try)]
public class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.UtcNow;
}
```

You can set the registration type at three levels. The generator uses the first value that it finds in this sequence:

1. The `Using` property of the service attribute.
2. The `Using` property of `[DependencyModule]`. This value is applicable to all services of the module.
3. The `DependencyModules_RegistrationType` MSBuild property. This value is applicable to all modules of the project.

If no level sets a value, the registration type is `Add`.

The generator reads the text of the `Using` value. Write the value as `RegistrationType.Try`, or as `Try` with a `using static` directive. If you write the full name of the enum or use a constant, the generator uses `Add` and gives no diagnostic.

## Registration sequence

In one module, the generator writes the registrations in this sequence:

1. Registrations without an environment condition are before registrations with an environment condition.
2. `Add` and `TryEnumerable` registrations are before `Try` and `Replace` registrations. For this step, the generator reads the `Using` property of the service attribute and the MSBuild property. It does not read the `Using` property of `[DependencyModule]`.
3. If the `Order` value of registration A is less than the `Order` value of registration B, A is first.
4. If two registrations have the same `Order` value, the generator compares the class names to set the sequence.

Thus a `Try` or `Replace` registration is after the `Add` registrations of the same module. It can change the result of these `Add` registrations.

The registrations from [conventions](./conventions.md) are after all registrations from service attributes of the module. Thus a `Try` or `Replace` service attribute does not change the result of a convention registration of the same module.

When a service type has more than one registration, `GetService` gives the instance from the last registration. `GetServices` gives the instances from all registrations. The `Order` property sets the position of a registration in the module. The default value is 0. Values less than 0 are permitted.

```csharp
using DependencyModules.Runtime.Attributes;

namespace Shop;

public interface IDiscountRule
{
    decimal Apply(decimal price);
}

[SingletonService(Order = 1)]
public class StandardDiscount : IDiscountRule
{
    public decimal Apply(decimal price) => price;
}

[SingletonService(Order = 2)]
public class SaleDiscount : IDiscountRule
{
    public decimal Apply(decimal price) => price * 0.9m;
}
```

In this example, `GetService<IDiscountRule>()` gives `SaleDiscount`.

Write `Order` as a number, for example `Order = 1000`. The generator reads the text of the value. If you use a constant or `1_000`, the `Order` value is 0, and the generator gives no diagnostic.

For the sequence of registrations from more than one module, refer to [Module load sequence](./modules.md#module-load-sequence).

## Cross-wired services

`[CrossWireService]` registers a class as each interface in its declaration. It also registers the class as the class type. The interface registrations get the instance from the registration of the class type. Thus, for the `Singleton` and `Scoped` lifetimes, all these registrations give the same instance in a scope.

```csharp
using DependencyModules.Runtime.Attributes;

namespace Shop;

public interface IInventoryReader
{
    int Count(string sku);
}

public interface IInventoryWriter
{
    void Set(string sku, int count);
}

[CrossWireService]
public class Inventory : IInventoryReader, IInventoryWriter
{
    private readonly Dictionary<string, int> _counts = new();

    public int Count(string sku) => _counts.GetValueOrDefault(sku);

    public void Set(string sku, int count) => _counts[sku] = count;
}
```

The class declaration must contain one or more interfaces. If the class gets its interfaces only from a base class, the generator writes no registration and gives no diagnostic.

The default lifetime is `Singleton`. To set a different lifetime, set the `Lifetime` property, for example `[CrossWireService(Lifetime = ServiceLifetime.Scoped)]`.

`[CrossWireService]` also has the `Realm`, `Using`, and `Key` properties. It has no `As` property and no `Order` property. `Realm` has the same function as on the other service attributes.

If you set `Using` on the attribute, all registrations of the class use this value. If you do not set `Using`, the interface registrations use the value of the module or of the MSBuild property. The registration of the class type then uses `Add`.

::: warning
Set `Using` on `[CrossWireService]` only to `Add` or `Replace`. If `Using` is `Try` or `TryEnumerable`, the generated code does not compile. If the module or the MSBuild property sets `TryEnumerable`, `AddModule` throws an `ArgumentException`. Do not set `Key` on `[CrossWireService]`. If you set `Key`, the generated code does not compile.
:::

The generator cannot cross-wire a generic class. It gives the warning DM0014 and does not register the class. For a generic class, you can use one of the other service attributes.

## Factory methods

You can put a service attribute on a static method. The generator calls the method to make the instance. The return type of the method is the service type.

```csharp
using DependencyModules.Runtime.Attributes;

namespace Shop;

public interface ITaxTable
{
    decimal Rate(string region);
}

public class FixedTaxTable(decimal rate) : ITaxTable
{
    public decimal Rate(string region) => rate;
}

public static class TaxFactories
{
    [SingletonService]
    public static ITaxTable CreateTaxTable(IClock clock) => new FixedTaxTable(0.2m);
}
```

The generator gets each parameter of the method from the service provider. If the method has one parameter of type `IServiceProvider`, the method gets the service provider.

The method must be `static`, and it must be `public` or `internal`. Write the access modifier. The generator ignores a `private` method, a `protected` method, and a method that is not `static`. It gives no diagnostic for these methods.

## Generic services

If a generic class implements a generic interface with the same type parameters, the generator writes an open generic registration.

```csharp
using DependencyModules.Runtime.Attributes;

namespace Shop;

public interface IRepository<T>
{
    List<T> Items { get; }
}

[ScopedService]
public class Repository<T> : IRepository<T>
{
    public List<T> Items { get; } = new();
}
```

The service provider can then give `IRepository<T>` for each type `T`.

A class that implements a closed interface, for example `IRepository<string>`, gets a closed registration. You can also set `As` to an open generic type, for example `As = typeof(IRepository<>)`.

## Classes that the generator cannot register

The generator cannot make an instance of an abstract class or a static class. If you put a service attribute on such a class, the generator gives the warning DM0002 and does not register the class. An abstract type can have a registration from a class that is not abstract, or from a factory method.

## Records, nested classes, and partial classes

A service can be a record class. A service can also be a nested class.

If a service class has more than one partial declaration, the generator reads only the declaration that has the service attribute. It reads the attributes, the base types, and the constructors from this declaration. Put the interfaces and the [environment attributes](./environments.md) on that declaration.

## JSON serializer contexts

The generator can register `JsonSerializerContext` classes as `IJsonTypeInfoResolver`. To do this for one module, set `RegisterJsonSerializers = true` on `[DependencyModule]`. To do this for all modules of a project, set the `DependencyModules_RegisterGenerator` MSBuild property to `true`.

```csharp
using System.Text.Json.Serialization;
using DependencyModules.Runtime.Attributes;

namespace Shop;

public record OrderMessage(string OrderId, decimal Total);

[JsonSourceGenerationOptions]
[JsonSerializable(typeof(OrderMessage))]
public partial class ShopJsonContext : JsonSerializerContext;

[DependencyModule(RegisterJsonSerializers = true)]
public partial class MessagingModule;
```

The generator registers each class that has `[JsonSourceGenerationOptions]` and no service attribute. The registration is transient and gives the `Default` property of the context.

## Realms

If a service has a realm, only the module of that realm registers the service. For more information, refer to [Realms](./modules.md#realms).
