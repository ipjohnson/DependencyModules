# API

This page shows the public types that applications and tests use. The generated code also uses types from the `DependencyModules.Runtime.Helpers` namespace. Your code does not usually call these types.

## `DependencyModules.Runtime`

### `ServiceCollectionExtensions`

| Method | Function |
| --- | --- |
| `AddModule<T>(this IServiceCollection services)` | Makes an instance of `T` and loads it. `T` must implement `IDependencyModule` and have a constructor without parameters. |
| `AddModule(this IServiceCollection services, IDependencyModule module)` | Loads the module. |
| `AddModules(this IServiceCollection services, params IDependencyModule[] modules)` | Loads the modules in one operation. |
| `AddModules(this IServiceCollection services, IModuleEnvironment? environment, params IDependencyModule[] modules)` | Loads the modules with the environment. |

The return value of all these methods is the service collection.

To call the last method with a `null` environment, cast the argument: `(IModuleEnvironment?)null`.

Guide: [Load a module](../guide/modules.md#load-a-module).

### `ModuleEnvironment`

`ModuleEnvironment` implements `IModuleEnvironment` and `IEnumerable<KeyValuePair<string, string?>>`.

| Member | Function |
| --- | --- |
| `ModuleEnvironment(string environmentName, IReadOnlyDictionary<string, string?>? values = null)` | Makes an environment that reads environment variables for the keys that are not in `values`. |
| `ModuleEnvironment(bool fallBackToEnvironmentVariables, string environmentName, IReadOnlyDictionary<string, string?>? values = null)` | Makes an environment. If the first argument is `false`, the environment does not read environment variables. |
| `EnvironmentName` | The name of the environment. |
| `Value(string name)` | Gives the value for the key, or `null`. |
| `Add(string key, string? value)` | Adds a value, or replaces the value for the key. |
| `static CreateDefault()` | Gives a new instance of the environment of the process. |
| `static None` | An environment with an empty name and no values. |

Guide: [Environments](../guide/environments.md).

## `DependencyModules.Runtime.Attributes`

For the attributes, refer to [Attributes](./attributes.md).

`BaseServiceAttribute` and `CrossWireServiceAttribute` implement `IServiceRegistrationAttribute`. This interface has the `As`, `Key`, `Lifetime`, and `Using` properties. Your code can use the interface to read a service attribute at run time. The generator does not use this interface to find attributes.

## `DependencyModules.Runtime.Interfaces`

| Interface | Members | Function |
| --- | --- | --- |
| `IDependencyModule` | `LoadModule`, `PopulateServiceCollection(IServiceCollection)`, `GetModules()` | The interface of all modules. The generator implements it on each module. If `LoadModule` is `false`, the module does not load. `GetModules()` gives more modules to load. |
| `IDependencyModuleProvider` | `GetModule()` | Gives a module. The generated module attributes implement it. |
| `IServiceCollectionConfiguration` | `ConfigureServices(IServiceCollection)`, `ConfigureDecorators(IServiceCollection)` | Registration code in a module. |
| `IEnvironmentServiceCollectionConfiguration` | `ConfigureServices(IServiceCollection, IModuleEnvironment)` | Registration code in a module that reads the environment. |
| `IModuleEnvironment` | `EnvironmentName`, `Value(string)` | The environment. |
| `IModuleEnvironmentProvider` | `ProvideEnvironment(MethodInfo testMethod)` | Gives the environment for a test. |

`IDependencyModule` also has members with the `Internal` prefix. The generated code uses them. Do not call these members from your code. They have `[EditorBrowsable(EditorBrowsableState.Never)]`. Thus IntelliSense does not show them in a project that references the package.

## `DependencyModules.Runtime.Features`

| Type | Function |
| --- | --- |
| `IDependencyModuleFeature<TFeature>` | A module that gets the loaded modules that implement `TFeature`. Members: `Order` (default 0) and `HandleFeature(IServiceCollection, IEnumerable<TFeature>)`. |
| `IDependencyModuleApplicatorProvider` | Gives the feature applicators of a module. The generator implements it. |
| `IFeatureApplicator` | Gives the loaded modules to a feature handler. Members: `Order` and `Apply(IServiceCollection, IReadOnlyList<IDependencyModule>)`. |
| `FeatureApplicator<TFeature>` | The `IFeatureApplicator` that the generated code uses. |

Guide: [Module features](../guide/extending.md#module-features).

## `DependencyModules.Runtime.Conventions`

| Interface | Function |
| --- | --- |
| `IConventionModule` | A module with conventions. Member: `Conventions(IConventionDefinitions conventions)`. |
| `IConventionDefinitions` | Starts a convention: `RegisterAll<TService>()`, `RegisterAll(Type serviceType)`, `RegisterAll()`. |
| `IConventionRegistration` | The calls of a convention chain. |

The `IConventionRegistration` calls:

| Group | Calls |
| --- | --- |
| Lifetime | `AsSingleton()`, `AsScoped()`, `AsTransient()` |
| Shape | `AsSelf()`, `AsSelfWithInterfaces()`, `AlsoAsSelf()`, `As<TService>()`, `AsMatchingInterface()` |
| Namespace filters | `InNamespaceOf<TMarker>()`, `InNamespaces(params string[])`, `InExactNamespaces(params string[])`, `NotInNamespaceOf<TMarker>()`, `NotInNamespaces(params string[])` |
| Name filters | `WithName(params string[])`, `WithoutName(params string[])` |
| Attribute filters | `WithAttribute<TAttribute>()`, `WithoutAttribute<TAttribute>()` |
| Selection | `IncludeBaseClasses()`, `InAssemblyOf<TMarker>()` |
| Registration | `WithKey(object)`, `Using(RegistrationType)` |
| Environment | `IfEnvironment(params string[])`, `IfNotEnvironment(params string[])`, `IfEnvironmentValue(string)`, `IfEnvironmentValue(string, string)`, `IfNotEnvironmentValue(string)`, `IfNotEnvironmentValue(string, string)` |

The generator reads these calls when you compile. The methods do not run.

Guide: [Conventions](../guide/conventions.md).

## `DependencyModules.Runtime.Interception`

| Type | Function |
| --- | --- |
| `IInterceptor` | `TResult Intercept<TResult>(InvocationContext<TResult> context)` |
| `IAsyncInterceptor` | `ValueTask<TResult> InterceptAsync<TResult>(AsyncInvocationContext<TResult> context)` |
| `IAsyncEnumerableInterceptor` | `IAsyncEnumerable<TItem> InterceptStream<TItem>(StreamInvocationContext<TItem> context)` |
| `InvocationContext<TResult>` | `Caller`, `Arguments`, `Proceed()` |
| `AsyncInvocationContext<TResult>` | `Caller`, `Arguments`, `ProceedAsync()` |
| `StreamInvocationContext<TItem>` | `Caller`, `Arguments`, `Proceed()` |
| `CallerInfo` | `ServiceType`, `MemberName`. `ToString()` gives `Service.Member`. |
| `IArguments` | `Count`, the indexer `this[int]` with get and set, `NameAt(int)` |
| `NoResult` | The result type of a member that has no return value. |

The `InvocationState` classes are for the generated wrappers.

Guide: [Interception](../guide/interception.md).

## `DependencyModules.Runtime.Helpers`

The generated code uses these types:

| Type | Function |
| --- | --- |
| `DependencyRegistry<T>` | Keeps the registrations and decorators of module `T`, and loads modules. |
| `DecoratorHelper` | Adds decorators and interceptor wrappers to the service collection. |
| `DecoratorRegistration` | A decorator with its `Order`. |
| `EnvironmentConditions` | Examines the environment conditions. |

## `DependencyModules.Testing.Attributes.Interfaces`

| Interface | Function |
| --- | --- |
| `IModuleTestAttribute` | `ModuleTypes`. The `[ModuleTest]` attributes implement it. |
| `ITestMethodContext` | `Method` and `Attributes` of the test that runs. |
| `ITestServiceSetupAttribute` | Adds registrations for a test. |
| `IServiceProviderBuilderAttribute` | Builds the service provider of a test. |
| `ITestStartupAttribute` | Runs code after the service provider is available. |
| `ITestParameterValueProvider` | Registers services for a parameter and gives the value of the parameter. |
| `IInjectValueAttribute` | Gives constructor arguments for a parameter value. |
| `IMockSupportAttribute` | Makes mocks for `[Mock]`. |
| `ISharedTestRegistration` | Selects the shared services. |
| `ITestContainerSource` | `CreateAsync()` builds a new service provider for the test. |
| `IOrderedAttribute` | `Order`, with the default value 10. |

Guide: [Testing](../guide/testing.md).

## `DependencyModules.Testing.Impl`

| Type | Function |
| --- | --- |
| `TestParameterResolver` | Gets the values of the test parameters. The test packages use it. |
| `TestContainerSource` | The `ITestContainerSource` implementation. |
| `SharedRegistrations` | Finds the shared services of a test. |
| `AttributeUtility` | Finds the attributes of a test on the method, the class, and the assembly. |
