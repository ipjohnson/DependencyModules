# More service providers in a test

A test can make more service providers from the same registrations. For example, a test can start two instances of an application that use one mock message bus. The `ITestContainerSource` service makes these service providers.

## Make a service provider

Add an `ITestContainerSource` parameter to the test. Call `CreateAsync()` to get a new service provider.

```csharp
using DependencyModules.Runtime.Attributes;
using DependencyModules.Testing.Attributes.Interfaces;
using DependencyModules.xUnit.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cluster.Tests;

public interface INodeCounter
{
    int Count { get; }

    void Add();
}

[SingletonService]
public class NodeCounter : INodeCounter
{
    public int Count { get; private set; }

    public void Add() => Count++;
}

[DependencyModule]
public partial class ClusterModule;

public class TwoNodeTests
{
    [ModuleTest(typeof(ClusterModule))]
    public async Task EachNodeHasItsCounter(ITestContainerSource source)
    {
        var first = await source.CreateAsync();
        var second = await source.CreateAsync();

        first.GetRequiredService<INodeCounter>().Add();

        Assert.Equal(1, first.GetRequiredService<INodeCounter>().Count);
        Assert.Equal(0, second.GetRequiredService<INodeCounter>().Count);
    }
}
```

Each call to `CreateAsync()` builds a new service provider from the service collection of the test. The new service provider makes new instances of the singletons that are not shared. The `ITestStartupAttribute` attributes run for each new service provider. The test package disposes all these service providers at the end of the test.

## Shared services

The new service providers give the same instance of a shared service. These services are shared:

- The type of each parameter of the test method. This includes the `[Mock]` parameters.
- Each `[TestExport]` with `Shared = true`.
- Each service type in the `SharedServices` property of an `ISharedTestRegistration` attribute.

`IServiceProvider` is not shared. A type from `IsolatedServices` is not shared. For more information, refer to [Change which services are shared](#change-which-services-are-shared).

```csharp
using DependencyModules.Testing.Attributes.Interfaces;
using DependencyModules.xUnit.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cluster.Tests;

public class SharedCounterTests
{
    [ModuleTest(typeof(ClusterModule))]
    public async Task TheParameterIsShared(ITestContainerSource source, INodeCounter counter)
    {
        var node = await source.CreateAsync();

        node.GetRequiredService<INodeCounter>().Add();

        Assert.Equal(1, counter.Count);
    }
}
```

In this example, `counter` is a parameter. Thus the new service provider gives the same `INodeCounter` instance.

## How the test package shares a service

At the first `CreateAsync()` call, the test package gets the instances of all shared services from the service provider of the test. The new service providers then give these instances. The results are as follows:

- For a singleton service, the service provider of the test and all new service providers give the same instance.
- For a transient service, all new service providers give one instance. This instance is not the instance of the test parameter. The service provider of the test continues to make new instances.
- The test package makes an instance of each shared service at the first `CreateAsync()` call, also if the test does not use the service.
- If the service provider of the test cannot make a shared service, the test package does not share this service. It gives no message.
- A parameter type without a registration is not in the new service providers. For example, a class that the test package makes with `ActivatorUtilities` is not in the new service providers.
- If a shared type has registrations with a key and without a key, the new service providers get only the instances without a key.

A `[TestExport]` with `Shared = true` is shared, also for a transient lifetime. A `[TestExport]` without `Shared = true` is also shared if the test has a parameter of that type. If the export is not shared, each new service provider makes new instances of the export.

## Change which services are shared

The `ISharedTestRegistration` interface in `DependencyModules.Testing.Attributes.Interfaces` selects the shared services. An attribute that implements it can have these members:

| Member | Default | Function |
| --- | --- | --- |
| `Shared` | `true` | On a parameter attribute, `false` makes the parameter not shared. On a different attribute, `true` shares the types in `SharedServices`. |
| `SharedServices` | Empty | Types to share. |
| `IsolatedServices(MethodInfo testMethod)` | Empty | Types that are not shared, also when they are parameters. The test package reads this member only from attributes on the test method, the test class, and the assembly. |

`[Mock]`, `[InjectValues]`, `[Shared]`, and `[TestExport]` implement this interface. `[Shared]` on a parameter does not change the result, because the test package shares all parameters by default.

An attribute can use `IsolatedServices` to make a type not shared. Each service provider then makes a different instance of that type, also when the test has a parameter of that type. For example, a test harness can use `IsolatedServices` for a type that must have a different instance in each service provider.
