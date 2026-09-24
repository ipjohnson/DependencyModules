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

If you set `As` to a capability interface, the generator uses it. `[CrossWireService]` does not use this list. It registers all interfaces that the class declares.

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

`TryEnumerable` cannot add a registration if its implementation type is the service type. A class that you register as itself makes such a registration. Each factory method also makes one. For these registrations, `AddModule` gives an `ArgumentException`. Use `Add` or `Try` for them.

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

The generator reads the value of `Using`, not its text. Thus you can also write the full name of the enum member or use a constant.

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

The generator reads the value of `Order`, not its text. Thus you can also use a constant or digit separators, for example `Order = 1_000`.

For the sequence of registrations from more than one module, refer to [Module load sequence](./modules.md#module-load-sequence).

## Cross-wired services

`[CrossWireService]` registers a class as each interface that the class declares. It also registers the class as the class type. The interface registrations get the instance from the registration of the class type. Thus, for the `Singleton` and `Scoped` lifetimes, all these registrations give the same instance in a scope.

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

The generator reads the interfaces from all partial declarations of the class. It does not cross-wire the interfaces of a base class. If the class declares no interface, the generator registers only the class type. If the class then gets interfaces from a base class, the generator also gives the warning DM0024. To cross-wire these interfaces, write them in the declaration of the class.

The default lifetime is `Singleton`. To set a different lifetime, set the `Lifetime` property, for example `[CrossWireService(Lifetime = ServiceLifetime.Scoped)]`.

`[CrossWireService]` also has the `Realm`, `Using`, and `Key` properties. It has no `As` property and no `Order` property. `Realm` has the same function as on the other service attributes. If you set `Key`, all registrations of the class use the key.

The registration of the class type and the interface registrations use the same registration type. The generator selects it in the sequence of [Registration type](#registration-type). For `TryEnumerable`, the registration of the class type uses `TryAdd`. `TryAddEnumerable` cannot add a registration that has its service type as its implementation type.

The generator cannot cross-wire a generic class. It gives the warning DM0014 and does not register the class. For a generic class, you can use one of the other service attributes.

You can also put `[CrossWireService]` on a static factory method. The generator then registers the return type of the method, and it cross-wires each interface that the return type declares.

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

The method must be `static`, and the generated code must be able to call it. Thus the method must be `public` or `internal`. A method without an access modifier is `private`. The classes that contain the method must not be `private` or `protected`. If the generated code cannot call the method, the generator gives the warning DM0023 and does not register the method.

Do not set `Key` on the attribute of a factory method. The generated keyed registration does not call the method. The service provider then gives an exception when you get the service.

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

The service provider uses only a `public` constructor. If a class has only `internal` constructors, the generator gives no warning. The service provider then cannot make an instance of the class. Make a constructor `public`, or set `GenerateFactories = true` on the module. A generated factory can call an `internal` constructor.

## Records, nested classes, and partial classes

A service can be a record class. A service can also be a nested class.

If a service class has more than one partial declaration, the generator reads only the declaration that has the service attribute. It reads the attributes, the base types, and the constructors from this declaration. Put the interfaces and the [environment attributes](./environments.md) on that declaration. `[CrossWireService]` is different. It reads the interfaces of all partial declarations.

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
