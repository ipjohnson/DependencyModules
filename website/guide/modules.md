# Modules

A module is a partial class or a partial record with the `[DependencyModule]` attribute. The generator writes the other part of the module. The module then adds its services to an `IServiceCollection`.

```csharp
using DependencyModules.Runtime.Attributes;

namespace Catalog;

[DependencyModule]
public partial class CatalogModule;
```

A module must be `partial`. If it is not partial, the generator gives the error DM0003. A module must be at the namespace level. If you declare a module in a different class, the generator gives the error DM0017 and does not write the other part of the module.

The generator reads the attributes of a module only from the declaration that has `[DependencyModule]`. If the module has more than one partial declaration, put the module attributes and the `IDependencyModuleFeature<T>` interfaces on that declaration.

## Services in a module

A module registers these services:

- Each service in the same project that does not set `Realm`.
- Each service that sets `Realm` to the module.
- Each class that a convention of the module selects. For more information, refer to [Conventions](./conventions.md).

A module can use the module of a different project to get the services of that project. For more information, refer to [Module dependencies](#module-dependencies). A convention can also register classes from a referenced assembly.

If a project has more than one module, each module registers all services that do not set `Realm`. If you load two of these modules, the services have two registrations. [Realms](#realms) can divide the services of a project between modules.

## Load a module

The `ServiceCollectionExtensions` class in the `DependencyModules.Runtime` namespace has these methods:

| Method | Result |
| --- | --- |
| `AddModule<T>()` | Makes an instance of `T` and loads it. `T` must have a constructor without parameters. |
| `AddModule(module)` | Loads the module instance. |
| `AddModules(params modules)` | Loads the module instances in one operation. |
| `AddModules(environment, params modules)` | Loads the module instances with the environment that you give. For more information, refer to [Environments](./environments.md). |

```csharp
using Catalog;
using DependencyModules.Runtime;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

services.AddModule<CatalogModule>();

var provider = services.BuildServiceProvider();
```

When a module loads, its dependencies also load.

In one load operation, each module loads one time. The load operation uses the `Equals` method to compare two modules. For more information, refer to [Module equality](#module-equality). If you call `AddModule` two times with the same module, the module loads two times and its registrations occur two times.

## Module dependencies

The generator writes an attribute for each module. The name of the attribute is the name of the module and the suffix `Attribute`. For `CatalogModule`, the attribute is `[CatalogModule]`.

To make a module use a different module, put the attribute of the other module on the module class:

```csharp
using DependencyModules.Runtime.Attributes;

namespace Shop;

[DependencyModule]
[Catalog.CatalogModule]
public partial class ShopModule;
```

When `ShopModule` loads, `CatalogModule` also loads. The dependency can be in a different project or in a NuGet package. Circular dependencies are permitted. Each module loads one time.

To make the generator write no attribute for a module, set `GenerateAttribute = false`:

```csharp
using DependencyModules.Runtime.Attributes;

namespace Shop;

[DependencyModule(GenerateAttribute = false)]
public partial class InternalToolsModule;
```

The generated attribute is `partial`. To add interfaces or members to the attribute, write a partial declaration of the attribute class.

## Module load sequence

The load sequence has an effect on which registration is the last for a service type. `GetService` gives the instance from the last registration.

Before the modules add their registrations, the load operation makes a list of modules. It examines the modules that you give, in the sequence of your list. After it examines a module, it examines the dependencies of that module, in the sequence of the module attributes. When the load operation examines a module for the first time, it puts the module at the start of the list. When it finds a module again, it does not change the list.

Then the modules add their registrations, from the start of the list to the end. Thus the module that the load operation examines first adds its registrations last.

This list shows the results:

- If you load one module, the module adds its registrations after all its dependencies. Thus a module can replace a service of a dependency with an `Add` registration of the same service type.
- In one `AddModules` call, the first module in your list adds its registrations last.
- If you call `AddModule` more than one time, each call makes a different list. The modules of the last call add their registrations last.

```csharp
using DependencyModules.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Shop;

var services = new ServiceCollection();

// PaymentModule adds its registrations last.
services.AddModules(new PaymentModule(), new ShippingModule());
```

A dependency keeps the position where the load operation first finds it. For example, `ShopModule` has the dependency `CatalogModule`. In `AddModules(new CatalogModule(), new ShopModule())`, the load operation examines `CatalogModule` first. Thus `CatalogModule` adds its registrations after `ShopModule`.

After the modules of one load operation add their services, the decorators of these modules change the registrations. A decorator does not change the registrations that a subsequent load operation adds. For more information, refer to [Decorators](./decorators.md).

## Realms

If a service has a realm, only the module of that realm registers the service. Set `Realm` on the service attribute to the type of the module:

```csharp
using DependencyModules.Runtime.Attributes;

namespace Shop;

public interface IShippingRates
{
    decimal Rate(string country);
}

[SingletonService(Realm = typeof(ShippingModule))]
public class ShippingRates : IShippingRates
{
    public decimal Rate(string country) => 5m;
}

[DependencyModule]
public partial class ShippingModule;
```

Only `ShippingModule` registers `ShippingRates`. The other modules of the project do not register it.

A module with `OnlyRealm = true` registers only the services that set `Realm` to that module. It also registers the classes that its conventions select. It does not register the other services without a realm.

```csharp
using DependencyModules.Runtime.Attributes;

namespace Shop;

public interface IPaymentProcessor
{
    bool Charge(decimal amount);
}

[SingletonService(Realm = typeof(PaymentModule))]
public class PaymentProcessor : IPaymentProcessor
{
    public bool Charge(decimal amount) => true;
}

[DependencyModule(OnlyRealm = true)]
public partial class PaymentModule;
```

Realms are also applicable to decorators and interceptors. A convention registers its services only in the module that declares the convention.

## Module parameters

A module can have constructor parameters and properties with a `set` accessor. The generated attribute has the same constructor parameters and the same properties. Thus the module that uses the attribute can give the values.

```csharp
using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Shop;

public record MailSettings(string Host, int Port, string? Sender);

[DependencyModule(OnlyRealm = true)]
public partial class MailModule : IServiceCollectionConfiguration
{
    private readonly string _host;
    private readonly int _port;

    public MailModule(string host, int port)
    {
        _host = host;
        _port = port;
    }

    public string? Sender { get; set; }

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(new MailSettings(_host, _port, Sender));
    }

    public override bool Equals(object? obj) =>
        obj is MailModule other
        && other._host == _host
        && other._port == _port
        && other.Sender == Sender;

    public override int GetHashCode() => HashCode.Combine(_host, _port, Sender);
}

[DependencyModule]
[MailModule("smtp.example.com", 25, Sender = "shop@example.com")]
public partial class NotificationModule;
```

The generator puts the `public`, `internal`, and `protected internal` properties that have a `set` accessor on the attribute. It does not put `static` properties on the attribute.

The generator examines only the modifiers of the property. It does not examine the modifiers of the `set` accessor. If the `set` accessor is `private`, the generated attribute does not compile. The generator also examines the properties of nested classes in the module. If a nested class has a property that the generator puts on the attribute, the generated attribute does not compile.

When the attribute does not set a property, the result is as follows:

- A reference-type property keeps the value from the module, for example the initial value of the property.
- A value-type property gets the default value of its type, for example 0. The property does not keep the initial value from the module.
- A nullable value-type property, for example `int?`, also gets the default value of the value type. The property of the attribute has the type `int`. Thus the module property gets 0, not `null`.

### Module equality

A module class that you declare gets a generated `Equals` method and a generated `GetHashCode` method. The generated `Equals` method compares only the module type. Thus, two instances of the same module type are the same module.

If the module declares a method with the name `Equals`, the generator does not write these methods. The generator examines only the declaration that has `[DependencyModule]`. Declare `Equals` and `GetHashCode` in that declaration. If you declare `Equals` in a different partial declaration, the compiler gives the error CS0111.

The load operation calls `Equals(object)`. If the module declares only a different `Equals` method, for example `IEquatable<T>.Equals(T)`, two instances are always two modules.

If a module has properties that the generator puts on the attribute and no `Equals` method, the generator gives the warning DM0018. Two instances with different values are then one module. Only the first instance loads. If you declare `Equals` and `GetHashCode`, two instances with different values can load, as shown in the `MailModule` example.

The generated `ApplicationModule` does not get these methods.

## Registration code in the module

A module can add registrations with code. Implement one or more of these interfaces from `DependencyModules.Runtime.Interfaces`:

| Interface | Method | When it runs |
| --- | --- | --- |
| `IServiceCollectionConfiguration` | `ConfigureServices(IServiceCollection services)` | After the generated registrations of the module. |
| `IServiceCollectionConfiguration` | `ConfigureDecorators(IServiceCollection services)` | After all decorators of all modules. This method is optional. |
| `IEnvironmentServiceCollectionConfiguration` | `ConfigureServices(IServiceCollection services, IModuleEnvironment environment)` | After `IServiceCollectionConfiguration.ConfigureServices`. It gets the environment. |

```csharp
using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Shop;

public record ShopSettings(string Environment);

[DependencyModule(OnlyRealm = true)]
public partial class SettingsModule : IEnvironmentServiceCollectionConfiguration
{
    public void ConfigureServices(IServiceCollection services, IModuleEnvironment environment)
    {
        services.AddSingleton(new ShopSettings(environment.EnvironmentName));
    }
}
```

## Extension method for a module

Set `GenerateUseMethod` to make the generator write an extension method for `IServiceCollection`. The method has the name that you give. The parameters of the method are the constructor parameters of the module. The method calls `AddModules`.

```csharp
using DependencyModules.Runtime.Attributes;

namespace Shop;

[DependencyModule(OnlyRealm = true, GenerateUseMethod = "AddReporting")]
public partial class ReportingModule(string connectionString)
{
    public string ConnectionString => connectionString;
}
```

The generator puts the method in the `ReportingModuleExtensions` class in the same namespace:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Shop;

var services = new ServiceCollection();

services.AddReporting("Server=reports");
```

## The generated application module

The generator can write a module for an application project. The generated module has the name `ApplicationModule` and is in the root namespace of the project. The generator writes it when these conditions are true:

- The project has a `Program.cs` file in the project folder.
- The `DependencyModules_AutoGenerateModule` MSBuild property is not `false`.

```csharp
using DependencyModules.Runtime;
using WebShop;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddModule<ApplicationModule>();

var app = builder.Build();

app.Run();
```

The registrations of `ApplicationModule` are as follows:

- If the project declares a module that is partial, is not realm-only, and has no constructor parameters, `ApplicationModule` loads that module. If the project declares more than one such module, the generator compares their full names. `ApplicationModule` then loads the first module.
- If the project declares no such module, `ApplicationModule` registers the services of the project.

To add a module from a different project to `ApplicationModule`, put the module attribute on the assembly in `Program.cs`:

```csharp
using DependencyModules.Runtime;
using Catalog;
using WebShop;

[assembly: CatalogModule]

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddModule<ApplicationModule>();

var app = builder.Build();

app.Run();
```

The generator reads assembly-level module attributes only from `Program.cs`. If you put one in a different file of an application project, the generator gives the error DM0019 and ignores the attribute. An assembly-level attribute must also have a `using` directive for its namespace. If the directive is missing, the generator gives the warning DM0016.

These two diagnostics are only for an attribute without its namespace, for example `[assembly: CatalogModule]`. A `using` directive is not necessary for an attribute with its full name, for example `[assembly: Catalog.CatalogModule]`. In a file that is not `Program.cs`, the generator ignores this attribute and gives no diagnostic.

The top-level statements of `Program.cs` can also call a static method of a module. `ApplicationModule` then also loads that module. These conditions are necessary:

- The statement only calls the method. It does not give the result to a variable.
- The module has a constructor without parameters.
- The module is from a referenced project or package. The generator cannot see the `IDependencyModule` interface of a module from the same project, because this interface is in generated code.

The generated `ApplicationModule` is a partial class. To add registration code to it, declare `partial class ApplicationModule` in the root namespace without `[DependencyModule]`. Then implement `IServiceCollectionConfiguration`.

If you declare a module with the name `ApplicationModule` and `[DependencyModule]` in the root namespace, the generator uses your module. It does not write a different `ApplicationModule`. The generator then ignores the assembly-level module attributes and the static calls in `Program.cs`, and it gives no diagnostic.

## Records as modules

A module can be a partial record:

```csharp
using DependencyModules.Runtime.Attributes;

namespace Shop;

[DependencyModule]
public partial record AuditModule;
```

A record module uses the equality of the record. The generator does not write `Equals` or `GetHashCode` for it.
