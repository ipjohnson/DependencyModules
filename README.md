# DependencyModules

DependencyModules is a C# source generator package that uses attributes to create
dependency injection registration modules. These packages can then be used to populate 
an IServiceCollection instance.

### Installation

```
dotnet add package DependencyModules.Runtime
dotnet add package DependencyModules.SourceGenerator
```

### Service Attributes 

* `[DependencyModule]` - used to attribute class that will become dependency module
* `[SingletonService]` - registers service as `AddSingleton`
* `[ScopedService]` - registers service as `AdddScoped`
* `[TransientService]` - registers service as `AddTransient`

```
// Registration example
[DependencyModule]
public partial class MyDeps { }

[SingletonService(ServiceType = typeof(ISomeService)]
public class SomeClass : ISomeService { }

[TransientService]
public class OtherService
{
  public OtherService(ISomeService service) { ... }
}
```
### Container Instantiation

`AddModule` - method adds modules to service collection

```
var serviceCollection = new ServiceCollection();

serviceCollection.AddModule<MyDeps>();

var provider = serviceCollection.BuildServiceProvider();

var service = provider.GetService<OtherService>();
```

### Module Re-use

DependencyModules creates a `ModuleAttribute` class that can be used to apply sub dependencies.

```
// Modules can be re-used with the generated attributes
[DependencyModule]
[MyDeps.Module]
public partial class AnotherModule { }
```

### Unit testing & Mocking

DependencyModules provides an xUnit extension to make testing much easier. 
It handles the population and construction of a service provider using specified modules.

```
> dotnet add package DependencyModules.Testing
> dotnet add package DependencyModules.Testing.NSubstitute

// applies module & nsubstitute support to all tests.
[assemlby: MyDeps.Module]
[assembly: NSubstituteSupport]

public class OtherServiceTests 
{
  [ModuleTest]
  public void SomeTest(OtherService test, [Mock]ISomeService service)
  {
     // assert implementation
  }
}
```
