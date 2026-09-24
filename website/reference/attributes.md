# Attributes

This page shows all attributes of the packages. For how to use each attribute, refer to the guide pages.

## Runtime attributes

Namespace: `DependencyModules.Runtime.Attributes`. Package: `DependencyModules.Runtime`.

### `[DependencyModule]`

Identifies a partial class or a partial record as a module. Targets: class, assembly.

| Property | Type | Default | Function |
| --- | --- | --- | --- |
| `OnlyRealm` | `bool` | `false` | If the value is `true`, the module registers only the services that set `Realm` to this module and the classes that its conventions select. |
| `Using` | `RegistrationType` | The MSBuild property or `Add` | The registration type for the services of the module. |
| `GenerateAttribute` | `bool` | `true` | If the value is `false`, the generator does not write a module attribute. |
| `GenerateUseMethod` | `string?` | `null` | The name of an `IServiceCollection` extension method that the generator writes for the module. |
| `GenerateFactories` | `bool` | The MSBuild property or `false` | If the value is `true`, the registrations of the module use generated factories. |
| `RegisterJsonSerializers` | `bool` | The MSBuild property or `false` | If the value is `true`, the module registers the classes that have `[JsonSourceGenerationOptions]` as `IJsonTypeInfoResolver`. |

If the module does not set `Using`, `GenerateFactories`, or `RegisterJsonSerializers`, the generator uses the applicable MSBuild property. For the properties, refer to [MSBuild properties](./msbuild.md).

Guide: [Modules](../guide/modules.md).

### `[SingletonService]`, `[ScopedService]`, `[TransientService]`

Registers a class, or the return value of a static method, with the lifetime in the name. Targets: class, method. You can put more than one on a class.

| Property | Type | Default | Function |
| --- | --- | --- | --- |
| `As` | `Type?` | `null` | The service type. Without `As`, the generator selects the service type. |
| `Key` | `object?` | `null` | The key of a keyed registration. |
| `Using` | `RegistrationType` | Not set | The registration type. If it is not set, the generator uses the value of the module, then the MSBuild property, then `Add`. |
| `Realm` | `Type?` | `null` | The module that registers the service. |
| `Order` | `int` | `0` | The position of the registration in the module. |

Guide: [Services](../guide/services.md).

### `[CrossWireService]`

Registers a class as each interface in its declaration and as the class type. All the registrations use the instance of the class registration. If the class declaration contains no interface, the generator writes no registration. Targets: class, method. On a method, the generator writes no registration.

| Property | Type | Default | Function |
| --- | --- | --- | --- |
| `Lifetime` | `ServiceLifetime` | `Singleton` | The lifetime of the registrations. |
| `Key` | `object?` | `null` | Do not set it. If you set `Key`, the generated code does not compile. |
| `Using` | `RegistrationType` | Not set | The registration type. Only `Add` and `Replace` are permitted. If it is not set, refer to [Cross-wired services](../guide/services.md#cross-wired-services). |
| `Realm` | `Type?` | `null` | The module that registers the service. |

Guide: [Cross-wired services](../guide/services.md#cross-wired-services).

### `[Decorator]`

Identifies a class as a decorator. Targets: class.

| Property | Type | Default | Function |
| --- | --- | --- | --- |
| `Service` | `Type?` | `null` | The service type to decorate. Without `Service`, the generator uses the first constructor parameter with a type that the class declaration contains. |
| `Order` | `int` | `0` | The position of the decorator. If the value of decorator A is less than the value of decorator B, B is the outer decorator. |
| `Realm` | `Type?` | `null` | The module that uses the decorator. |
| `Implementation` | `Type?` | `null` | The implementation to decorate. Without `Implementation`, the decorator changes all registrations of the service type. |

Guide: [Decorators](../guide/decorators.md).

### `[Decorate]`

Adds a decorator from a module. Targets: class (the module class). You can put more than one on a module.

| Constructor parameter or property | Type | Function |
| --- | --- | --- |
| `service` | `Type` | The service type to decorate. |
| `decorator` | `Type` | The decorator type. |
| `Order` | `int` | The position of the decorator. The default is 0. |

Guide: [Decorate from a module](../guide/decorators.md#decorate-from-a-module).

### `[Intercept]`

Adds interceptors to a class. Targets: class. You can put more than one on a class.

| Constructor parameter or property | Type | Default | Function |
| --- | --- | --- | --- |
| `interceptors` | `params Type[]` | Not applicable | The interceptor types. The first type gets the call first. |
| `Service` | `Type?` | `null` | The interface to intercept. |
| `Order` | `int` | `0` | The position in the decorator sequence. |
| `Realm` | `Type?` | `null` | The module that uses the interception. |
| `Lifetime` | `ServiceLifetime` | `Singleton` | The lifetime of the interceptor registrations. |
| `Members` | `InterceptedMembers` | `All` | The members to intercept. |

Guide: [Interception](../guide/interception.md).

### Environment attributes

Targets: class. Put them on a service class or on a `[Decorator]` class. You can also put them on a class in the project that a convention selects. The generator ignores them on a decorator that `[Decorate]` adds and on a class from a referenced assembly.

| Attribute | Constructor | More than one on a class |
| --- | --- | --- |
| `[IfEnvironment]` | `(params string[] environmentNames)` | No |
| `[IfNotEnvironment]` | `(params string[] environmentNames)` | No |
| `[IfEnvironmentValue]` | `(string key)` or `(string key, string value)` | Yes |
| `[IfNotEnvironmentValue]` | `(string key)` or `(string key, string value)` | Yes |

Guide: [Environments](../guide/environments.md).

### Enums

`RegistrationType`:

| Value | Generated call |
| --- | --- |
| `Add` | `AddSingleton`, `AddScoped`, `AddTransient`, or the keyed methods. |
| `Try` | `TryAddSingleton`, `TryAddScoped`, `TryAddTransient`, or the keyed methods. |
| `TryEnumerable` | `TryAddEnumerable` |
| `Replace` | `Replace` |

`InterceptedMembers` is a flags enum: `Methods = 1`, `Properties = 2`, `Indexers = 4`, `Events = 8`, `All = 15`.

## The generated module attribute

For each module, the generator writes an attribute with the name of the module and the suffix `Attribute`. The attribute is in the namespace of the module.

- Targets: class, assembly, method, parameter. You can put more than one on an item.
- The constructor has the constructor parameters of the module.
- The properties are the `public`, `internal`, and `protected internal` properties of the module that have a `set` accessor.
- The attribute implements `IDependencyModuleProvider`.

Guide: [Module dependencies](../guide/modules.md#module-dependencies).

## Test attributes

Namespace: `DependencyModules.Testing.Attributes`. Package: `DependencyModules.Testing`.

| Attribute | Targets | Function |
| --- | --- | --- |
| `[Mock]` | Parameter | Registers a mock for the parameter type and gives it to the parameter. |
| `[InjectValues(params object[] value)]` | Parameter | Gives constructor arguments when the test package makes the parameter value. |
| `[Shared]` | Parameter | Identifies the parameter as shared. The test package shares the parameters by default. It does not share `IServiceProvider` parameters or the types from `IsolatedServices`. |
| `[TestExport(Type service)]` | Method, class, assembly | Registers a service for the test. More than one is permitted. |

`[TestExport]` properties:

| Property | Type | Default | Function |
| --- | --- | --- | --- |
| `Implementation` | `Type?` | The service type | The class that the registration makes. |
| `Lifetime` | `ServiceLifetime` | `Transient` | The lifetime of the registration. |
| `Shared` | `bool` | `false` | If the value is `true`, the service providers from `ITestContainerSource` give the same instance. |

Guide: [Testing](../guide/testing.md).

## Test framework attributes

| Attribute | Namespace | Targets | Constructor |
| --- | --- | --- | --- |
| `[ModuleTest]` (xUnit) | `DependencyModules.xUnit.Attributes` | Method | `()`, `(Type module)`, `(params Type[] modules)` |
| `[ModuleTest]` (NUnit) | `DependencyModules.NUnit.Attributes` | Method | `(params Type[] modules)` |
| `[ModuleTestCase]` (NUnit) | `DependencyModules.NUnit.Attributes` | Method | `(params object?[] arguments)` |

`[ModuleTestCase]` has the `TestName` property. You can put more than one `[ModuleTestCase]` on a method.

Guide: [xUnit](../guide/testing-xunit.md), [NUnit](../guide/testing-nunit.md).

## Mock support attributes

Targets: method, class, assembly.

| Attribute | Namespace |
| --- | --- |
| `[NSubstituteSupport]` | `DependencyModules.NSubstitute` |
| `[MoqSupport]` | `DependencyModules.Moq` |
| `[FakeItEasySupport]` | `DependencyModules.FakeItEasy` |

Guide: [Mocks](../guide/testing-mocking.md).
