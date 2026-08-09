# Testing registrations

## The problem

`[ModuleTest]` answers questions about **behaviour**: resolve a service, call it, assert on what it
did. Some questions are not about behaviour at all.

"Did my convention match exactly the three repositories I meant, and not the test double someone
added last week?" You cannot answer that by resolving one service — resolving works fine whether the
convention matched three types or thirty. The thing you want to inspect is the registration list
itself.

## How DependencyModules helps

There is nothing to learn here, and that is the point. Modules apply to a plain `IServiceCollection`,
so you build one and read it:

```csharp
using DependencyModules.Runtime;

var services = new ServiceCollection();

services.AddModules(new DataModule());

var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IRepository));

Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
Assert.Equal(typeof(SqlRepository), descriptor.ImplementationType);
```

No test-framework integration, no attributes — an ordinary `[Fact]` works.

## Pinning what a convention matched

This is the shape that earns its keep, because the interesting question about a
[convention](/guide/conventions) is usually *which types matched*:

```csharp
var registered = services
    .Where(d => d.ServiceType == typeof(IRepository))
    .Select(d => d.ImplementationType!.Name)
    .OrderBy(name => name)
    .ToArray();

Assert.Equal(["OrderRepository", "ProductRepository"], registered);
```

Assert on the **whole set**, not with `Assert.Contains`. A containment check passes happily while
your convention quietly picks up a fourth type that someone adds next year — which is precisely the
failure mode conventions have.

## Testing conditional registrations

Registrations gated on the [environment](/guide/environments) are decided **when the modules are
applied**, so the environment has to be supplied at that moment:

```csharp
var services = new ServiceCollection();

services.AddModules(new ModuleEnvironment("Development"), new ApplicationModule());

Assert.IsType<FakeEmailSender>(
    services.BuildServiceProvider().GetRequiredService<IEmailSender>());
```

::: warning Always name the environment
Supply nothing and the **process** environment is used, which defaults to `"Production"`. A test for
a development-only service that forgets this quietly tests the other branch and passes for the wrong
reason.
:::

Both sides fit in one theory:

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

## Testing decorator order

Reflecting over a decorator chain is painful and tells you little. Have each decorator contribute to
a string instead, and assert on the result:

```csharp
Assert.Equal("outer(inner(core))", provider.GetRequiredService<IOrdered>().Describe());
```

That reads as the nesting it describes, and fails with a message you can act on.

## Testing interceptors

Easiest through something the interceptor writes to:

```csharp
var provider = services.BuildServiceProvider();
var log = provider.GetRequiredService<InterceptLog>();

provider.GetRequiredService<IOrders>().Count("acme");

Assert.Equal(["intercepted Count"], log.Lines);
```

::: danger Build the provider once
`BuildServiceProvider()` called twice gives you two providers with two independent sets of
singletons. Resolve the service from one and the log from the other and you are comparing two
different instances — the assertion fails and the registration looks broken when it is not.

```csharp
var provider = services.BuildServiceProvider();   // once

var service = provider.GetRequiredService<IOrders>();
var log = provider.GetRequiredService<InterceptLog>();   // same provider, same singleton
```
:::

## Testing what a package scan found

A [referenced-assembly scan](/guide/scanning) is worth pinning, because a package upgrade can change
what matches without anything in your code changing:

```csharp
var policies = provider.GetServices<IPackagePolicy>()
    .Select(policy => policy.Name)
    .OrderBy(name => name)
    .ToArray();

Assert.Equal(["first", "second"], policies);
```

Remember that only `public` types cross an assembly boundary, so a scan finds strictly less than the
same convention would in your own project.
