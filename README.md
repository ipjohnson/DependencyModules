### DependencyModules

* Handles all service collection registration `AddSingleton`
* Creates re-usable packages of registration methods, including an attribute allowing for easy re-use
* xUnit attributes that allow for easy unit testing and mocking

```
[DependencyModule]
public partial class Module { }

[SingletonService(ServiceType = typeof(ISomeService)]
public class SomeClass { }

[TransientService]
public class OtherService
{
  public OtherService(ISomeService service) { }
}

****************

var serviceCollection = new ServiceCollection();

serviceCollection.AddModule<Module>();

var provider = serviceCollection.BuildServiceProvider();

var service = provider.GetService<OtherService>();
```

```
// unit tests
[assemlby: LoadModules(typeof(Module))]

public class SomeClassTests 
{
  [ModuleTest]
  public void SomeTest(OtherService test)
  {
     // assert implementation
  }
}
```