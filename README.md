# DependencyModules

DependencyModules is a C# source generator package that uses attributes to create
dependency injection registration modules. These modules can then be used to populate 
an IServiceCollection instance.

## Installation

```csharp
dotnet add package DependencyModules.Runtime
dotnet add package DependencyModules.SourceGenerator
```

## Service Attributes 

* `[DependencyModule]` - used to attribute class that will become dependency module (must be partial)
* `[SingletonService]` - registers service as `AddSingleton`
* `[ScopedService]` - registers service as `AdddScoped`
* `[TransientService]` - registers service as `AddTransient`
* `[CrossWireService]` - register implementation and interfaces with the same lifetime

```csharp
// Registration example
[DependencyModule]
public partial class MyModule { }

// registers SomeClass implementation for ISomeService
[SingletonService]
public class SomeClass : ISomeService 
{
  public string SomeProp => "SomeString";
}

// registers OtherSerice implementation
[TransientService]
public class OtherService
{
  public OtherService(ISomeService service)
  { 
    SomeProp = service.SomeProp;
  }
  public string SomeProp { get; }
}
```

Note: `[DependencyModule]` is not required for [Top-level](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/program-structure/top-level-statements) statement applications.
## Container Instantiation

* `AddModule` - method adds root module to service collection
* `AddModules` - add a list of modules to the service collection

```csharp
var serviceCollection = new ServiceCollection();

serviceCollection.AddModule<MyModule>();
// or
serviceCollection.AddModules(new MyModule(), ...);

var provider = serviceCollection.BuildServiceProvider();

var service = provider.GetService<OtherService>();
```

Note: to avoid duplicate modules it's recommend to only call AddModule(s) once in an application and never inside a Module.
## Factories

Sometimes it's not possible to construct all types through normal registration.
Factories can be registered with a module using the registration attributes.

```csharp
public class SomeClass : ISomeInterface {
  public SomeClass(IDep one, IDepTwo two, DateTime dateTime) { ... }
  
  [SingletonService]
  public static ISomeInterface Factory(IDep one, IDepTwo two) {
    return new SomeClass(one, two, DateTime.Now());   
  }
}
```
## Module Re-use

DependencyModules creates an `Attribute` class that can be used to apply sub dependencies.

```csharp
// Modules can be re-used with the generated attributes
[DependencyModule]
[MyModule]
public partial class AnotherModule { }
```

## Parameters

Sometimes you want to provide extra registration for your module. 
This can be achieved by adding a constructor to your module or optional properties. 
Note these parameters and properties will be correspondingly implemented in the module attribute.

```csharp
[DependencyModule]
public partial class SomeModule(bool someFlag) : IServiceCollectionConfiguration 
{
  public string OptionalString { get; set; } = "";
  
  public void ConfigureServices(IServiceCollection services) 
  {
    if (someFlag) 
    {
      // custom registration
    } 
  }
}

[DependencyModule]
[SomeModule(true, OptionalString = "otherString")]
public partial class SomeOtherModule 
{

}
```

## Module Features
Because module configuration happens before the dependency injection container is instantiate it's impossible to use the container for configuration.
To support configuration discovery before registration, the feature interface can be 
implemented in modules and be passed to a handler at registration time. Features applied before service and decorators.

```csharp
// feature interface
public interface IFeature { }

[DependencyModule]
public partial class ModuleImeplementation : ISomeFeature { }

[DependencyModule]
[ModuleImeplementation]
public partial class FeatureHandlerModule : IDependencyModuleFeature<ISomeFeature> 
{
  public void HandleFeature(IServiceCollection collection, IEnumerable<ISomeFeature> features) 
  {
      // invoked with service collection and one instance of the ModuleImplementation class
  }
}
```

## Managing duplicate registration

By default a module will only be loaded once, assuming attributes are used or the modules are specified in the same `AddModules` call. Seperate calls to `AddModule` will result in modules being loaded multiple times. If a module uses parameters it can be useful to load a module more than once. That can be accompilished by overriding the `Equals` and `GetHashcode` methods to allow for multiple loads.

```csharp
// CustomModule will be loaded as long as someString is unique.
// Duplicate modules with the same someString value will be ignored
[DependencyModule]
public partial class CustomModule(string someString) : IServiceCollectionConfiguration 
{
  public void ConfigureServices(IServiceCollection services) 
  {
    // custom logic
  }

  public override bool Equals(object obj)
  {
    if (obj is CustomModule module)
    {
      return someString.Equals(module.someString);
    }

    return false;
  }

  public override int GetHashCode()
  {
    return someString.GetHashCode();
  }
}
```

Services will be registered using an `Add` method by default. This can be overriden using the `With` property on individual service or at the `DepedencyModule` level. Note: the following are valid registration types Add, Try, TryEnumerable, Replace.

```csharp
[SingletonService(With = RegistrationType.Try)]
public class SomeService { }

[DependencyModule(With = RegistrationType.Try)]
public partial class SomeModule { }
```

## Realm

By default, all dependencies are registered in all modules within the same assembly. 
The realm allows the developer to scope down the registration within a given module.

```csharp
// register only dependencies specifically marked for this realm
[DependencyModule(OnlyRealm = true)]
public partial class AnotherModule { }

[SingletonService(Realm = typeof(AnotherModule))]
public class SomeDep : ISomeInterface { }
```

## Keyed Registration

Registration attributes have a `Key` property that allows for specifying the key at registration time.

```csharp
[SingletonService(Key = "SomeKey")]
public class KeyService : IKeyService { }

// yields this registration line
services.AddKeyedSingleton(typeof(IKeyService), "SomeKey", typeof(KeyService));
```

## Autogenerated Modules

To simplify registration for [Top-Level](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/program-structure/top-level-statements) statement applications, an `ApplicationModule` will be autogenerated.

```csharp
[assembly: SomeOtherModule]

var serviceCollection = new ServiceCollection();

// load SomeOtherModule as well as all registrations in the current project
serviceCollection.AddModule<ApplicationModule>();
```

## Unit testing & Mocking

DependencyModules provides an xUnit extension to make testing much easier. 
It handles the population and construction of a service provider using specified modules.

```csharp
> dotnet add package DependencyModules.xUnit
> dotnet add package DependencyModules.xUnit.NSubstitute

// applies module & nsubstitute support to all tests.
// test attributes can be applied at the assembly, class, and test method level
[assemlby: MyModule]
[assembly: NSubstituteSupport]

public class OtherServiceTests 
{
  [ModuleTest]
  public void SomeTest(OtherService test, [Mock]ISomeService service)
  {
     service.SomeProp.Returns("some mock value");
     Assert.Equals("some mock value", test.SomeProp);
  }
}

```
## Implementation

Behind the scenes the library generates registration code that can be used with any `IServiceCollection` compatible DI container.

Example generated code for [SutModule.cs](integ-tests/SutProject/SutModule.cs)
```csharp
    // SutModule.Dependencies.g.cs
    public partial class SutModule
    {
        private static int moduleField = DependencyRegistry<SutModule>.Add(ModuleDependencies);

        private static void ModuleDependencies(IServiceCollection services)
        {
            services.AddTransient(typeof(IDependencyOne), typeof(DependencyOne));
            services.AddSingleton(typeof(IGenericInterface<>), typeof(GenericClass<>));
            services.AddScoped(typeof(IScopedService), typeof(ScopedService));
            services.AddSingleton(typeof(ISingletonService), typeof(SingletonService));
            services.AddSingleton(typeof(IGenericInterface<string>), typeof(StringGeneric));
        }
    }

    // SutModule.Modules.g.cs
    public partial class SutModule : IDependencyModule
    {
        static SutModule()
        {
        }

        // this method loads all dependencies into IServiceCollection.
        public void PopulateServiceCollection(IServiceCollection services)
        {
            DependencyRegistry<SutModule>.LoadModules(services, this);
        }

        void IDependencyModule.InternalApplyServices(IServiceCollection services)
        {
            DependencyRegistry<SutModule>.ApplyServices(services);
        }

        public override bool Equals(object? obj)
        {
            return obj is SutModule;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(base.GetHashCode());
        }
    }
    
    public class SutModuleAttribute : System.Attribute, IDependencyModuleProvider
    {
        public IDependencyModule GetModule()
        {
            var newModule = new SutModule();
            return newModule;
        }
    }    
```
