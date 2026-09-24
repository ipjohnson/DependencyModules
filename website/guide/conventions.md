# Conventions

A convention registers all classes that agree with a set of conditions. You do not put an attribute on each class. When you compile, the generator reads the convention. It then writes one registration for each class that the convention selects.

You do not add a package for conventions. The convention types are in `DependencyModules.Runtime`, and `DependencyModules.SourceGenerator` reads the conventions.

## Declare a convention

Implement `IConventionModule` on a module. Write the conventions in the `Conventions` method.

```csharp
using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Conventions;

namespace Billing;

public interface IInvoiceRule
{
    bool Accepts(decimal total);
}

public class MinimumTotalRule : IInvoiceRule
{
    public bool Accepts(decimal total) => total > 0;
}

public class MaximumTotalRule : IInvoiceRule
{
    public bool Accepts(decimal total) => total < 10_000;
}

[DependencyModule]
public partial class BillingModule : IConventionModule
{
    public void Conventions(IConventionDefinitions conventions)
    {
        conventions.RegisterAll<IInvoiceRule>().AsSingleton();
    }
}
```

This convention registers `MinimumTotalRule` and `MaximumTotalRule` as `IInvoiceRule`.

The generator reads the `Conventions` method when you compile. The method does not run. Thus these conditions are applicable to the method:

- The method must have a body with statements.
- Each statement starts with a `RegisterAll` call on the parameter of the method.
- Each statement continues with a chain of calls on the result of `RegisterAll`.
- The arguments must be values that the compiler knows, for example string literals, constants, and `nameof` expressions.

If the generator cannot read a statement, it gives the error DM0009 and does not use the statement.

If one argument of a call is not a value that the compiler knows, the generator gives DM0009 for the statement. For example, `IfEnvironmentValue(Keys.Feature, "on")` gives DM0009 if `Keys.Feature` is a `static readonly` field. Use string literals or `const` fields for these arguments.

You can implement the method as a public method or as an explicit interface implementation: `void IConventionModule.Conventions(IConventionDefinitions conventions)`. If a type has the two methods, the generator reads the explicit interface implementation.

The type that implements `IConventionModule` must have `[DependencyModule]`. If it does not have this attribute, the generator gives the error DM0009. The generator does this only if the project has one or more modules.

## Select classes by service type

`RegisterAll` sets the service type for the convention:

| Call | Selected classes |
| --- | --- |
| `RegisterAll<IService>()` | Classes that implement `IService`. |
| `RegisterAll(typeof(IService))` | The same as `RegisterAll<IService>()`. |
| `RegisterAll(typeof(IHandler<>))` | Classes that implement a type of `IHandler<>`. |
| `RegisterAll()` | Classes that agree with the filters. A service type is not necessary. |

For an open generic service type, the convention registers each class as each closed type that the class implements. A generic class can also implement the open generic type, for example `Handler<T> : IHandler<T>`. The convention then writes an open generic registration. The class must give its type parameters to the interface without changes and in the same sequence. The convention does not register a class such as `Handler<T> : IHandler<Order, T>`.

A class implements the service type for a convention when one of these conditions is true:

- The class declaration contains the service type.
- The class declaration contains an interface that derives from the service type.

A convention does not select a class if only its base class implements the service type. `IncludeBaseClasses()` also selects these classes.

The service type must be an interface. A convention for a class type selects no classes. The generator then gives the warning DM0005.

### Candidate classes

A convention examines only the classes of the project that contains the module. A class is a candidate when all these conditions are true:

- It is a class, a record class, or a record struct.
- It is not `static` and not `abstract`.
- The generated code can use it. Thus it is not `private`, `protected`, or `file`, and it is not in a `private` or `protected` class. An `internal` class and a `protected internal` class are candidates.
- It does not have a service attribute or `[Decorator]`.

A class with a service attribute keeps the registration from its attribute. The convention does not register this class again.

A nested class can be a candidate. A nested class without an access modifier is `private`. Thus the convention does not select it.

A selected class must have a `public` constructor, or no declared constructor. The service provider uses only `public` constructors. If the module uses [generated factories](./aot.md#generated-factories), the generated code calls the constructor. Then an `internal` or `protected internal` constructor is also correct. This is not true for a class with `[Intercept]`, because the generator does not write a factory for it. If the class has no constructor that its registration can use, the generator gives the warning DM0006 and does not register the class.

## Lifetime

Each convention must call one lifetime method:

- `AsSingleton()`
- `AsScoped()`
- `AsTransient()`

If a convention has no lifetime or more than one lifetime, the generator gives the error DM0009.

## Registration shape

By default, a convention registers each class as the service type that it selected. These calls change the registrations:

| Call | Registrations |
| --- | --- |
| No call | Each selected interface. For a service type that is an open generic type, each closed type that the class implements. |
| `AsSelf()` | The class type only. |
| `AsSelfWithInterfaces()` | The class type and each interface of the class. The convention does not include the interfaces in `System` namespaces. |
| `AlsoAsSelf()` | Each selected interface and the class type. |
| `As<TService>()` | The type `TService` only. |
| `AsMatchingInterface()` | The interface with the name `I` and the class name. If the class has no such interface, the convention does not register the class. |

For `AsSelfWithInterfaces()`, the interfaces of the class are the interfaces in the class declaration and the interfaces that they derive from. If the convention calls `IncludeBaseClasses()`, the convention also includes the interfaces of the base classes. For `AsSelfWithInterfaces()`, the convention removes the interfaces in `System` namespaces. This is also true for the service type of the convention. If the convention uses a different registration shape, it registers a service type in a `System` namespace.

If a convention calls `AsSelfWithInterfaces()` or `AlsoAsSelf()`, the interface registrations get the instance from the registration of the class type. Thus, for the `Singleton` and `Scoped` lifetimes, all these registrations give the same instance in a scope. If `AsSelfWithInterfaces()` finds no interface, it registers only the class type.

The generator cannot cross-wire a generic class. If a convention with `AlsoAsSelf()` or `AsSelfWithInterfaces()` selects a generic class, the generator gives the warning DM0014 and does not register that class. Select the generic classes with a different convention that does not use these calls.

Use only one of `AsSelf()`, `AsSelfWithInterfaces()`, and `AlsoAsSelf()` in a convention. If you use more than one, the generator gives the error DM0009.

```csharp
using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Conventions;

namespace Billing;

public interface IExporter
{
    string Export(decimal total);
}

public class CsvExporter : IExporter
{
    public string Export(decimal total) => total.ToString();
}

[DependencyModule]
public partial class ExportModule : IConventionModule
{
    public void Conventions(IConventionDefinitions conventions)
    {
        conventions.RegisterAll<IExporter>().AlsoAsSelf().AsSingleton();
    }
}
```

In this example, the service provider gives the same instance for `IExporter` and `CsvExporter`.

## Filters

Filters decrease the number of selected classes. You can use more than one filter in a convention.

| Call | Result |
| --- | --- |
| `InNamespaceOf<TMarker>()` | Selects classes in the namespace of `TMarker` and in its nested namespaces. |
| `InNamespaces("A", "B")` | Selects classes in the given namespaces and in their nested namespaces. |
| `InExactNamespaces("A", "B")` | Selects classes in the given namespaces only. |
| `NotInNamespaceOf<TMarker>()` | Removes classes in the namespace of `TMarker` and in its nested namespaces. |
| `NotInNamespaces("A", "B")` | Removes classes in the given namespaces and in their nested namespaces. |
| `WithName("*Repository")` | Selects classes with a name that agrees with one of the patterns. |
| `WithoutName("*Fake")` | Removes classes with a name that agrees with one of the patterns. |
| `WithAttribute<TAttribute>()` | Selects classes that have the attribute. |
| `WithoutAttribute<TAttribute>()` | Removes classes that have the attribute. |

These conditions are applicable to filters:

- A nested namespace starts with the namespace and a period. The filter for `Shop.Orders` includes `Shop.Orders.Import`. It does not include `Shop.OrdersArchive`.
- In a name pattern, `*` agrees with zero or more characters. `?` agrees with one character. Name patterns are case-sensitive.
- If a name pattern contains a period, the filter compares the pattern with the full name of the class (the namespace and the class name). If the name pattern does not contain a period, the filter compares the pattern with the class name only.
- The name of a nested class contains the names of the classes around it, for example `Outer.Inner`.
- An attribute filter compares the attribute type. Thus the filter also finds an attribute that you write with its full name or with an alias.
- A class must agree with each type of filter that the convention has: namespace, name, and attribute.
- If a convention has more than one namespace filter that selects, the class must be in one of these namespaces.
- If a convention has more than one name pattern that selects, the class name must agree with one of these patterns.
- If a convention has more than one `WithAttribute` filter, the class must have all these attributes.
- The convention does not select a class that agrees with a filter that removes.

```csharp
using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Conventions;

namespace Billing.Storage;

public interface IStore
{
    string Name { get; }
}

public class InvoiceRepository : IStore
{
    public string Name => "invoices";
}

public class CustomerRepository : IStore
{
    public string Name => "customers";
}

public class RepositoryFake : IStore
{
    public string Name => "fake";
}

[DependencyModule]
public partial class StorageModule : IConventionModule
{
    public void Conventions(IConventionDefinitions conventions)
    {
        conventions
            .RegisterAll<IStore>()
            .InNamespaceOf<InvoiceRepository>()
            .WithName("*Repository")
            .AsScoped();
    }
}
```

### Selection by filters only

`RegisterAll()` without a service type selects classes only with filters. Two more conditions are applicable to this type of convention:

- The convention must have a filter that selects: a namespace filter, a name filter, or an attribute filter. Without such a filter, the convention could select all classes of the project. Thus the generator gives the error DM0009.
- The convention must set a registration shape, for example `AsSelf()`. Without a shape, the generator does not know the service type. Thus it gives the error DM0009.

```csharp
using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Conventions;

namespace Billing.Calculators;

public class TaxCalculator
{
    public decimal Tax(decimal total) => total * 0.2m;
}

public class FeeCalculator
{
    public decimal Fee(decimal total) => 1.5m;
}

[DependencyModule]
public partial class CalculatorModule : IConventionModule
{
    public void Conventions(IConventionDefinitions conventions)
    {
        conventions
            .RegisterAll()
            .InNamespaceOf<TaxCalculator>()
            .WithName("*Calculator")
            .AsSelf()
            .AsTransient();
    }
}
```

## Classes from a referenced assembly

`InAssemblyOf<TMarker>()` makes the convention examine the assembly that contains `TMarker`. The convention then does not examine the project. You can use `InAssemblyOf<TMarker>()` for a package or for a project that has no module.

```csharp
conventions.RegisterAll<IPolicy>().InAssemblyOf<PolicyMarker>().AsSingleton();
```

A convention examines one assembly. If a convention calls `InAssemblyOf` two times, the last call replaces the first call.

In a referenced assembly, a class is a candidate when all these conditions are true:

- It is a `public` class.
- It is not a nested class.
- It is not `abstract` and not `static`.
- It does not have a service attribute or `[Decorator]`.

A selected class must have a `public` constructor.

The generator reads the environment attributes of a class from a referenced assembly. The conditions of the class and the conditions of the convention are applicable. If a condition of the class has no name or no key, the generator gives the warning DM0012 at the convention statement.

A class from a referenced assembly has no location in your source code. Thus the generator shows its diagnostics, for example DM0010, at the convention statement.

## Keys and registration type

`WithKey(key)` registers each class as a keyed service. The generator writes the key into the generated code without changes. With `AlsoAsSelf()` or `AsSelfWithInterfaces()`, all registrations of a class use the key.

`Using(RegistrationType.Try)` sets the registration type. For the values, refer to [Registration type](./services.md#registration-type). The generator reads the value of the argument. Thus you can also write the full name of the enum member or use a constant. If the argument is not a value that the compiler knows, the generator gives the error DM0009.

```csharp
conventions.RegisterAll<IStore>().WithKey("archive").Using(RegistrationType.Try).AsScoped();
```

## Environment conditions

A convention can register its classes only in some environments. Use these calls:

| Call | The convention registers when |
| --- | --- |
| `IfEnvironment("Development", "Staging")` | The environment name is one of the names. |
| `IfNotEnvironment("Production")` | The environment name is not one of the names. |
| `IfEnvironmentValue("FEATURE_X")` | The environment has a value for the key. |
| `IfEnvironmentValue("FEATURE_X", "on")` | The value for the key is equal to the given value. |
| `IfNotEnvironmentValue("FEATURE_X")` | The environment has no value for the key. |
| `IfNotEnvironmentValue("FEATURE_X", "on")` | The value for the key is not equal to the given value. |

If a selected class has environment attributes, for example `[IfEnvironment("Development")]`, the conditions of the class are also applicable. This is also true for a class from a referenced assembly. All conditions must be true. For more information, refer to [Environments](./environments.md).

## Diagnostics

| ID | Severity | Cause |
| --- | --- | --- |
| DM0004 | Error | Two conventions in one module register the same class as the same service type. The generator does not write these two registrations. |
| DM0005 | Warning | A convention selects no classes. |
| DM0006 | Warning | A selected class has no constructor that its registration can use. |
| DM0009 | Error | The generator cannot read a convention statement. |
| DM0010 | Info | The generator registered a class from a convention. The message shows the service type and the module. |
| DM0012 | Warning | An environment condition of a selected class has no name or no key. |
| DM0014 | Warning | A convention with `AlsoAsSelf()` or `AsSelfWithInterfaces()` selects a generic class. |

If two conventions select one class for two different service types, this is not an error. The class then gets two registrations. For example, a class that implements `IFirstRole` and `ISecondRole` can have a singleton registration as `IFirstRole` and a scoped registration as `ISecondRole`.

## Decorators and interception

[Decorators](./decorators.md) are applicable to convention registrations. A generic decorator is applicable to each closed registration that a convention makes.

`[Intercept]` is not a service attribute. Thus a class with `[Intercept]` stays a candidate for a convention. The interceptors are then applicable to the convention registration. For more information, refer to [Interception](./interception.md).
