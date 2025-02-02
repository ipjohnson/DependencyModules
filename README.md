# DependencyModules

DependencyModules is a C# source generator package that uses attributes to create
dependency injection registration modules. These packages can then be used to populate 
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
## Container Instantiation

`AddModule` - method adds modules to service collection

```csharp
var serviceCollection = new ServiceCollection();

serviceCollection.AddModule<MyModule>();

var provider = serviceCollection.BuildServiceProvider();

var service = provider.GetService<OtherService>();
```

## Module Re-use

DependencyModules creates an `Attribute` class that can be used to apply sub dependencies.

```csharp
// Modules can be re-used with the generated attributes
[DependencyModule]
[MyModule.Attribute]
public partial class AnotherModule { }
```

## Parameters

Sometimes you want to provide extra registration for your module. 
This can be achieved by adding a constructor to your module or optional properties. 
Note these parameters and properties will be correspondingly implemented in the module attribute.

```csharp
[DependencyModule]
public partial class SomeModule : IServiceCollectionConfiguration 
{
  private bool _someFlag;
  public SomeModule(bool someFlag = false)
  {
    _someFlag = someFlag;
  }
  
  public string OptionalString { get; set; } = "";
  
  public void ConfigureServices(IServiceCollection services) 
  {
    if (_someFlag) 
    {
      // custom registration
    } 
  }
}

[DependencyModule]
[SomeModule.Attribute(true, OptionalString = "otherString")]
public partial class SomeOtherModule 
{

}
```

## Realm

By default, all dependencies are registered in all modules within the same assembly. 
The realm allows the developer to scope down the registration within a given module.

```csharp
// register only dependencies specifically marked for this realm
[DependencyModule(OnlyRealm = true)]
public partial class AnotherModule { }

[SingletonService(ServiceType = typeof(ISomeInterface), 
  Realm = typeof(AnotherModule))]
public class SomeDep : ISomeInterface { }
```

## Unit testing & Mocking

DependencyModules provides an xUnit extension to make testing much easier. 
It handles the population and construction of a service provider using specified modules.

```csharp
> dotnet add package DependencyModules.xUnit
> dotnet add package DependencyModules.xUnit.NSubstitute

// applies module & nsubstitute support to all tests.
// test attributes can be applied at the assembly, class, and test method level
[assemlby: MyModule.Attribute]
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
