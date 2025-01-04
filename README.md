### DependencyModules

* Handles all service collection registration `AddSingleton`
* Creates re-usable packages of registration methods, including an attribute allowing for easy re-use
* xUnit attributes that allow for easy unit testing and mocking

```
// Registration example
[DependencyModule]
public partial class Module { }

[SingletonService(ServiceType = typeof(ISomeService)]
public class SomeClass : ISomeService { }

[TransientService]
public class OtherService
{
  public OtherService(ISomeService service) { ... }
}

// Module usage example
var serviceCollection = new ServiceCollection();

serviceCollection.AddModule<Module>();

var provider = serviceCollection.BuildServiceProvider();

var service = provider.GetService<OtherService>();
```

```
// unit tests example
[assemlby: LoadModules(typeof(Module))]
[assembly: NSubstituteSupport()]

public class OtherServiceTests 
{
  [ModuleTest]
  public void SomeTest(OtherService test, [Mock]ISomeService service)
  {
     // assert implementation
  }
}
```