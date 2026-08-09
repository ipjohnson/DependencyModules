# Testing registrations

The xUnit package tests behaviour *through* the container. For assertions about the registrations
themselves — lifetimes, keys, how many types matched — build the collection directly and look at it.
No test framework integration is involved.

```csharp
using DependencyModules.Runtime;

var services = new ServiceCollection();

services.AddModules(new DataModule());

var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IRepository));

Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
Assert.Equal(typeof(SqlRepository), descriptor.ImplementationType);
```

## Testing a convention

This is the shape that matters most, because the interesting question about a
[convention](/guide/conventions) is usually *which types matched* rather than what one of them does.

```csharp
var registered = services
    .Where(d => d.ServiceType == typeof(IRepository))
    .Select(d => d.ImplementationType!.Name)
    .OrderBy(name => name)
    .ToArray();

Assert.Equal(["OrderRepository", "ProductRepository"], registered);
```

Assert on the **whole set** rather than using `Assert.Contains` — that is what catches a convention
quietly picking up an extra type.

## Testing conditional registrations

Registrations gated on the [environment](/guide/environments) are decided when the modules are
applied, so the environment has to be supplied at that point:

```csharp
var services = new ServiceCollection();

services.AddModules(new ModuleEnvironment("Development"), new ApplicationModule());

Assert.IsType<FakeEmailSender>(
    services.BuildServiceProvider().GetRequiredService<IEmailSender>());
```

::: warning Name the environment
Supply nothing and the process environment is used, which defaults to `"Production"`. A test for a
development-only service that forgets this tests the wrong branch.
:::

A theory covers both sides in one place:

```csharp
[Theory]
[InlineData("Development", typeof(FakeEmailSender))]
[InlineData("Production", typeof(SmtpEmailSender))]
public void SelectsTheSenderByEnvironment(string environment, Type expected) {
    var services = new ServiceCollection();

    services.AddModules(new ModuleEnvironment(environment), new ApplicationModule());

    Assert.IsType(expected, services.BuildServiceProvider().GetRequiredService<IEmailSender>());
}
```

## Testing decorators

Assert on the resolved type, and on the effect. For [ordering](/guide/decorators#ordering), have each
decorator contribute to a string — far clearer than reflecting over the chain:

```csharp
Assert.Equal("outer(inner(core))", provider.GetRequiredService<IOrdered>().Describe());
```

## Testing interceptors

Easiest through something the interceptor records:

```csharp
var provider = services.BuildServiceProvider();
var log = provider.GetRequiredService<InterceptLog>();

provider.GetRequiredService<IOrders>().Count("acme");

Assert.Equal(["intercepted Count"], log.Lines);
```

::: danger Build one provider
Building a provider twice gives you two independent sets of singletons. Resolving a service from one
and asserting on a singleton captured from another compares two different instances, and the failure
looks like the registration is broken.

```csharp
var provider = services.BuildServiceProvider();   // once

var service = provider.GetRequiredService<IOrders>();
var log = provider.GetRequiredService<InterceptLog>();   // same provider, same singleton
```

:::

## Testing what a package scan found

A [referenced-assembly scan](/guide/scanning) is worth pinning: a package upgrade can change what
matches.

```csharp
var policies = provider.GetServices<IPackagePolicy>()
    .Select(policy => policy.Name)
    .OrderBy(name => name)
    .ToArray();

Assert.Equal(["first", "second"], policies);
```

Remember only `public` types cross an assembly boundary, so a scan finds strictly less than the same
convention would in your own project.
