### DependencyModules

* Handles all service collection registration `AddSingleton`
* Creates re-usable packages of registration methods, including an attribute allowing for easy re-use
* xUnit attributes that allow for easy unit testing and mocking

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

// Module usage example
var serviceCollection = new ServiceCollection();

serviceCollection.AddModule<MyDeps>();

var provider = serviceCollection.BuildServiceProvider();

var service = provider.GetService<OtherService>();
```

```
// Modules can be re-used with the generated attributes
[DependencyModule]
[MyDeps.Module]
public partial class AnotherModule { }
```

```
// unit tests example
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
