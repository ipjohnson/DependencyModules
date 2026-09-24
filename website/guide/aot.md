# Native AOT and trimming

You can use DependencyModules in applications that you publish with Native AOT or with trimming. The generator finds the services when you compile. The generated code references each service type directly.

## The runtime package

`DependencyModules.Runtime` sets `IsAotCompatible` to `true`. Its build makes the trimming and AOT warnings IL2026, IL2055, IL2067, IL2072, IL2075, IL2087, and IL3050 into errors. The runtime does not examine assemblies to find services.

## Generated registrations

The generated code registers each class with its type, for example `services.AddSingleton(typeof(IClock), typeof(SystemClock))`. The service provider then calls the constructor of the class. The registration methods of `Microsoft.Extensions.DependencyInjection` tell the trimmer to keep the public constructors of the class.

Each generated method with registrations has a static field. The initializer of the field gives the method to the module. The field has a `[DynamicDependency]` attribute that identifies the method. Thus the trimmer keeps these methods.

The generated code examines the environment conditions at run time with `if` statements. Thus the generated code references all classes that have conditions, and the trimmer keeps these classes.

Decorators and interceptor wrappers use constructor calls in the generated code. They do not use reflection to make instances. The interceptor wrapper for a generic class is different. The service provider makes that wrapper from its type.

## Generated factories

The generator can also write a factory for each registration. The factory calls the constructor of the class in the generated code:

```csharp
services.AddSingleton(
    typeof(global::Shop.IPriceCalculator),
    provider => new global::Shop.PriceCalculator()
);
```

To use generated factories for all modules of a project, set the `DependencyModules_GenerateFactories` MSBuild property:

```xml
<PropertyGroup>
  <DependencyModules_GenerateFactories>true</DependencyModules_GenerateFactories>
</PropertyGroup>
```

To use generated factories for one module, set `GenerateFactories = true` on `[DependencyModule]`. If a module sets `GenerateFactories`, the generator uses the value of the module and not the MSBuild property.

For a factory, the generator selects the constructor in this sequence:

1. A constructor with `[ActivatorUtilitiesConstructor]`.
2. The primary constructor, if it has parameters.
3. The constructor with the most parameters.

Without generated factories, the service provider selects the constructor.

The generator does not use `private` constructors. It gets each constructor parameter from the service provider:

| Parameter | Generated call |
| --- | --- |
| `IServiceProvider` | The service provider. |
| A nullable type, for example `IClock?` | `GetService`. The value is `null` if there is no registration. |
| A parameter with `[FromKeyedServices("key")]` | `GetRequiredKeyedService`, or `GetKeyedService` for a nullable type. |
| All other parameters | `GetRequiredService`. |

The generator does not write factories for generic classes. It also does not write factories for the service types that a class with `[Intercept]` registers.

Each factory that the generator writes returns its class. Thus a decorator that sets `Implementation` finds the implementation of the registration. For more information, refer to [Decorate one implementation](./decorators.md#decorate-one-implementation).

## Test packages

`DependencyModules.Testing`, the test packages, and the mock packages use reflection to make modules, mocks, and test parameters.

Use these packages only in test projects. Do not publish them with Native AOT.
