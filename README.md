# DependencyModules

[![NuGet](https://img.shields.io/nuget/v/DependencyModules.Runtime.svg)](https://www.nuget.org/packages/DependencyModules.Runtime/)
[![build](https://github.com/ipjohnson/DependencyModules/actions/workflows/build-package.yaml/badge.svg)](https://github.com/ipjohnson/DependencyModules/actions/workflows/build-package.yaml)
[![coverage](https://raw.githubusercontent.com/ipjohnson/DependencyModules/badges/coverage.svg)](https://github.com/ipjohnson/DependencyModules/actions/workflows/build-package.yaml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/ipjohnson/DependencyModules/blob/main/LICENSE.txt)

**Dependency injection, decided at compile time.**

Declare registration next to the class it belongs to. A source generator writes the
`IServiceCollection` calls during the build — so nothing reflects, nothing scans at startup, and the
trimmer can follow every registration you declared.

📖 **[Full documentation](https://ipjohnson.github.io/DependencyModules/)**

## The problem

Every .NET application keeps a list like this, and nothing checks that it is complete:

```csharp
services.AddScoped<IOrderRepository, OrderRepository>();
services.AddSingleton<IEmailSender, SmtpEmailSender>();
// … another two hundred lines
```

Forget a line and you find out at run time, in the environment you deployed to. Reach for a runtime
scanner instead and you trade that for three new problems: you can no longer read what was
registered, the scan runs on every start, and the trimmer cannot see through reflection — so a
published, trimmed build registers nothing at all.

## What it looks like instead

Mark the class, and the registration is written for you during the build.

```csharp
[SingletonService]
public class SmtpEmailSender : IEmailSender { }

[DependencyModule]
public partial class ApplicationModule;
```

```csharp
var services = new ServiceCollection();

services.AddModule<ApplicationModule>();
```

That is the whole idea. Everything below builds on it.

## Install

```shell
dotnet add package DependencyModules.Runtime
dotnet add package DependencyModules.SourceGenerator
```

Requires .NET 8.0 or later. The packages ship both `net8.0` and `net10.0` assemblies, so a project on
either LTS release gets one built against its own framework.

→ [Getting started](https://ipjohnson.github.io/DependencyModules/guide/getting-started.html)

## What you can do with it

### Register by rule, resolved during the build

Declare a rule once and let it cover everything that fits — including the handler somebody adds next
year. The body never runs; it is read at compile time and turned into ordinary registration calls.

```csharp
[DependencyModule]
public partial class HandlerModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll(typeof(IRequestHandler<,>)).AsScoped();
        conventions.RegisterAll<IValidator>().InNamespaceOf<OrderMarker>().AsScoped();
    }
}
```

Assignability, namespaces, attributes and name globs all match — including types in a referenced
package.

→ [Conventions](https://ipjohnson.github.io/DependencyModules/guide/conventions.html) ·
[Scanning a package](https://ipjohnson.github.io/DependencyModules/guide/scanning.html)

### Decorate and intercept

Wrap a service with a decorator you write, or with a generated wrapper that routes every member
through an interceptor. Both compose with conventions, and both are ordered globally.

```csharp
[Decorator(Order = 10)] public class Retrying(IRepository inner) : IRepository { }
[Decorator(Order = 20)] public class Logging(IRepository inner)  : IRepository { }

// resolves as Logging(Retrying(SqlRepository))
```

→ [Decorators](https://ipjohnson.github.io/DependencyModules/guide/decorators.html) ·
[Interception](https://ipjohnson.github.io/DependencyModules/guide/interception.html)

### Gate registrations on the environment

A service, decorator or whole convention can exist only where it is wanted. Where the condition does
not hold the registration is never made — so the service resolves undecorated rather than being
wrapped by something that re-checks the environment on every call.

```csharp
[SingletonService]
[IfEnvironment("Development")]
public class ConsoleEmailSender : IEmailSender { }

[Decorator]
[IfEnvironment("Production")]
public class CircuitBreaker(IPaymentGateway inner) : IPaymentGateway { }
```

```csharp
conventions.RegisterAll<IAuditSink>().IfEnvironmentValue("AUDIT", "on").AsSingleton();
```

→ [Environments](https://ipjohnson.github.io/DependencyModules/guide/environments.html)

### Test against real modules, with mocks where you want them

The xUnit package builds a provider from the modules a test names and injects the services the test
asks for. Mocking comes from whichever library you already use — NSubstitute, Moq or FakeItEasy.

```csharp
[assembly: ApplicationModule]
[assembly: MoqSupport]

public class OrderServiceTests {
    [ModuleTest]
    public void SendsTheReceipt(OrderService orders, Mock<IEmailSender> email) {
        orders.Place(new Order());

        email.Verify(x => x.Send(It.IsAny<Receipt>()));
    }
}
```

The service under test is built against the same mock the test configures — no wiring in between.

→ [Testing modules](https://ipjohnson.github.io/DependencyModules/guide/testing.html) ·
[Mocks and values](https://ipjohnson.github.io/DependencyModules/guide/testing-mocks.html)

### Survive trimming and Native AOT

Each match is emitted as a literal `typeof()`, which the trimmer roots and which carries the
constructor along with it. The capability that breaks reflection-based scanners is the one that
works here.

→ [Trimming and AOT](https://ipjohnson.github.io/DependencyModules/guide/aot.html)

### Find out at build time, not at startup

A convention that matches nothing, a service that cannot be constructed, two conventions claiming one
service type — each is a `DM####` diagnostic in the IDE rather than an exception in production.

→ [Diagnostics reference](https://ipjohnson.github.io/DependencyModules/reference/diagnostics.html)

## Registrations you can read

There is no container graph to reason about. Set `EmitCompilerGeneratedFiles` and the file under
`obj/` is the ground truth:

```csharp
services.AddScoped(typeof(IRequestHandler<CreateOrder, OrderId>), typeof(CreateOrderHandler));
services.AddSingleton(typeof(IEmailSender), typeof(SmtpEmailSender));
```

Any `IServiceCollection`-compatible container works, because that is all the generator produces.

## Documentation

| | |
|---|---|
| [Getting started](https://ipjohnson.github.io/DependencyModules/guide/getting-started.html) | Install and register your first service |
| [Modules](https://ipjohnson.github.io/DependencyModules/guide/modules.html) | Composition, parameters, features, realms |
| [Registering services](https://ipjohnson.github.io/DependencyModules/guide/services.html) | Lifetimes, keys, factories, `As`, `Try`/`Replace` |
| [Conventions](https://ipjohnson.github.io/DependencyModules/guide/conventions.html) | Bulk registration by rule |
| [Decorators](https://ipjohnson.github.io/DependencyModules/guide/decorators.html) · [Interception](https://ipjohnson.github.io/DependencyModules/guide/interception.html) | Wrapping services |
| [Environments](https://ipjohnson.github.io/DependencyModules/guide/environments.html) | Conditional registration |
| [Testing](https://ipjohnson.github.io/DependencyModules/guide/testing.html) | Module tests, mocks, asserting registrations |
| [Trimming and AOT](https://ipjohnson.github.io/DependencyModules/guide/aot.html) | Publishing trimmed and Native AOT |
| [Extending](https://ipjohnson.github.io/DependencyModules/guide/extending.html) | Building your own generator on top |
| [Reference](https://ipjohnson.github.io/DependencyModules/reference/attributes.html) | Attributes, diagnostics, MSBuild properties |

## Packages

| Package | Purpose |
|---|---|
| `DependencyModules.Runtime` | Attributes, module interfaces, `AddModule` |
| `DependencyModules.SourceGenerator` | Generates the registration code |
| `DependencyModules.Conventions` | Convention-based registration |
| `DependencyModules.xUnit` | `[ModuleTest]` for xUnit v3 |
| `DependencyModules.NSubstitute` · `.Moq` · `.FakeItEasy` | Mocking support, pick one |
| `DependencyModules.Testing` | Shared test seam, referenced for you |
| `DependencyModules.SourceGenerator.Impl` | Source-only, for building your own generator |

## Something not registering?

The [troubleshooting guide](https://ipjohnson.github.io/DependencyModules/guide/troubleshooting.html)
covers reading the generated output and turning on the generator log, which together explain almost
every surprise. Please include both in any
[issue](https://github.com/ipjohnson/DependencyModules/issues) you open.

## Contributing

Issues and pull requests are welcome. See the
[changelog](https://github.com/ipjohnson/DependencyModules/blob/main/CHANGELOG.md) for release notes.

Licensed under the [MIT License](https://github.com/ipjohnson/DependencyModules/blob/main/LICENSE.txt).
