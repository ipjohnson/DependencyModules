# ![](https://raw.githubusercontent.com/ipjohnson/DependencyModules/main/assets/logo-readme.svg) DependencyModules

[![NuGet](https://img.shields.io/nuget/v/DependencyModules.Runtime.svg)](https://www.nuget.org/packages/DependencyModules.Runtime/)
[![build](https://github.com/ipjohnson/DependencyModules/actions/workflows/build-package.yaml/badge.svg)](https://github.com/ipjohnson/DependencyModules/actions/workflows/build-package.yaml)
[![coverage](https://raw.githubusercontent.com/ipjohnson/DependencyModules/badges/coverage.svg)](https://github.com/ipjohnson/DependencyModules/actions/workflows/build-package.yaml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/ipjohnson/DependencyModules/blob/main/LICENSE.txt)

**Your DI registrations, written as attributes and compiled into your assembly.**
No reflection, no assembly scanning, no startup cost — and Native AOT works, because
there is nothing left to trim away.

📖 **[Documentation](https://ipjohnson.github.io/DependencyModules/)** ·
[Getting started](https://ipjohnson.github.io/DependencyModules/guide/getting-started) ·
[Conventions](https://ipjohnson.github.io/DependencyModules/guide/conventions) ·
[Decorators](https://ipjohnson.github.io/DependencyModules/guide/decorators) ·
[Testing](https://ipjohnson.github.io/DependencyModules/guide/testing) ·
[AOT](https://ipjohnson.github.io/DependencyModules/guide/aot)

## The whole trick

You mark a class:

```csharp
[SingletonService]
public class SmtpEmailSender : IEmailSender;
```

At build time the generator writes the registration into your assembly:

```csharp
// ApplicationModule.Dependencies.g.cs
services.AddSingleton(
    typeof(global::MyApp.IEmailSender),
    typeof(global::MyApp.SmtpEmailSender)
);
```

That is the entire mechanism. The output is ordinary C# that you can read, grep, set a
breakpoint in, and check into a review. Nothing inspects your assembly at run time, so
there is no startup scan to pay for and nothing for the trimmer to guess about.

## Install

```shell
dotnet add package DependencyModules.Runtime
dotnet add package DependencyModules.SourceGenerator
```

Requires .NET 8.0 or later. The packages ship `net8.0` and `net10.0` assemblies, so a
project on either LTS gets one built against its own framework. Console applications also
want `Microsoft.Extensions.DependencyInjection`.

## Quick start

Mark the services, declare a module, load it once:

```csharp
// Services.cs
using DependencyModules.Runtime.Attributes;

namespace MyApp;

[SingletonService]
public class SmtpEmailSender : IEmailSender;

[ScopedService]
public class OrderRepository : IOrderRepository;
```

```csharp
// Program.cs
using MyApp;                       // the generated module lives in your root namespace
using DependencyModules.Runtime;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddModule<ApplicationModule>();

var provider = services.BuildServiceProvider();
```

`ApplicationModule` is generated for you in a project whose entry point is a top-level
`Program.cs`. Anywhere else — a class library, or a project that wants more than one module —
declare your own:

```csharp
[DependencyModule]
public partial class ApplicationModule;
```

Declaring one in a project that already gets a generated `ApplicationModule` merges with it rather
than colliding — and to add a `ConfigureServices` to the generated one, declare the partial
*without* `[DependencyModule]` and implement `IServiceCollectionConfiguration`.

A module must be `partial`, and must be declared directly in a namespace rather than nested
inside another type. Services marked with `[SingletonService]` and friends may be nested freely.

> The generated module takes the project's `RootNamespace`, and top-level statements sit
> in the global namespace — so a top-level `Program.cs` needs `using YourRootNamespace;`
> before it can name `ApplicationModule`.

## Registering forty things without writing forty attributes

Declare the rule once. It is matched by the compiler, against the types that exist at build
time:

```csharp
[DependencyModule]
public partial class HandlerModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll(typeof(IRequestHandler<,>)).AsScoped();

        conventions.RegisterAll(typeof(IValidator<>))
            .IncludeBaseClasses()
            .AlsoAsSelf()
            .AsScoped();
    }
}
```

Every handler in the project is registered against the closed interface it implements. Add a
handler tomorrow and it joins; delete one and the registration goes with it. A convention that
stops matching anything is a build warning rather than a runtime surprise.

The body of `Conventions` is never executed — it is read from source at compile time, which is
why only the documented calls can appear in it. See the
[conventions guide](https://ipjohnson.github.io/DependencyModules/guide/conventions).

## Composing modules

A module generates an attribute of the same name, so modules compose by attribute:

```csharp
[DependencyModule]
[DomainModule]
[InfrastructureModule(useInMemory: true, ConnectionName = "primary")]
public partial class ApiModule;
```

Constructor parameters and settable properties on a module are mirrored onto its generated
attribute, so a module can be configured by whoever composes it. For anything the attributes
cannot express, implement `IServiceCollectionConfiguration` and write the registrations by hand.

## Decorators and interception

Wrap a service without touching it or its callers. The first constructor parameter is the
wrapped instance; the rest resolve normally:

```csharp
[Decorator(Order = 2000)]
public class CachingRepository(IRepository inner, IMemoryCache cache) : IRepository;

[Decorator(Order = 1000)]
public class TracingRepository(IRepository inner, ILogger<TracingRepository> log) : IRepository;

// resolves as CachingRepository(TracingRepository(SqlRepository))
```

Lower orders sit closer to the implementation. Ordering is global across every module in an
`AddModule(s)` call, so an application's decorators can wrap those a library contributed —
by convention framework code uses 0–999 and application code starts at 1000.

For cross-cutting behaviour across every member of a service, `[Intercept]` generates a typed
wrapper rather than a dynamic proxy. See
[decorators and interception](https://ipjohnson.github.io/DependencyModules/guide/decorators).

## Testing

Tests receive their dependencies as method parameters, against the real registration graph:

```csharp
[assembly: ApplicationModule]
[assembly: NSubstituteSupport]

public class OrderTests {
    [ModuleTest]
    public async Task PlaceOrder_PricesThroughTheChannel(
        IRequestHandler<PlaceOrder, Order> handler,
        [Mock] IBookRepository books) {

        books.Find("isbn-1", Arg.Any<CancellationToken>())
            .Returns(new Book("isbn-1", 20m));

        var order = await handler.Handle(new PlaceOrder("isbn-1", 10), default);

        Assert.Equal(140m, order.Total);
    }
}
```

```shell
dotnet add package DependencyModules.xUnit        # or DependencyModules.NUnit
dotnet add package DependencyModules.NSubstitute  # or .Moq, or .FakeItEasy
```

Each test gets its own provider, so singletons cannot leak between them. See the
[testing guide](https://ipjohnson.github.io/DependencyModules/guide/testing).

## Native AOT

Verified end to end: a console application using conventions, keyed registrations, decorators,
a static factory and an intercepted open generic publishes to a **2.2 MB** self-contained
binary with **zero IL trim or AOT warnings**, behaving identically to the JIT build.

The one limitation is not this library's to fix: the container cannot close an open generic
over a value type without dynamic code, so `IRepository<Order>` resolves and `IRepository<int>`
throws. Setting `PublishAot` makes that fail in an ordinary `dotnet run` rather than only after
publishing. See the [AOT guide](https://ipjohnson.github.io/DependencyModules/guide/aot).

## Compared with runtime scanning

The registration work that Scrutor, container modules, or a hand-written `AddScoped` list
do when the application starts happens here at `dotnet build`:

| | Runtime scanning | DependencyModules |
|---|---|---|
| When registration is decided | First request to the container | `dotnet build` |
| A convention that matches nothing | Silent | [`DM0005`](https://ipjohnson.github.io/DependencyModules/reference/diagnostics) at build |
| A service that cannot be constructed | `InvalidOperationException`, eventually | [`DM0002`](https://ipjohnson.github.io/DependencyModules/reference/diagnostics) at build |
| Trimming / Native AOT | Types disappear; scanner finds nothing | Literal `typeof()`, so the trimmer keeps them |
| Startup cost | Proportional to assembly size | None |
| What actually got registered | Debugger, at run time | A file you can open |

The third row is the mechanism behind the AOT results above: a trimmer keeps what is
statically referenced, a type found only by reflection is not referenced, and an emitted
`typeof(CreateOrderHandler)` is.

## Feature reference

| | |
|---|---|
| `[SingletonService]` `[ScopedService]` `[TransientService]` | Register with the matching lifetime |
| `[CrossWireService]` | One instance shared across the implementation and its interfaces |
| `As = typeof(IFoo)` | Choose the service type explicitly |
| `Key = "primary"` | Keyed registration |
| `Using = RegistrationType.Try` | `Add`, `Try`, `TryEnumerable` or `Replace` |
| `Realm = typeof(SomeModule)` | Restrict a registration to one module |
| `Order = 10` | Where a registration sits in `IEnumerable<T>` |
| `[IfEnvironment("Development")]` | Register only in named environments |
| `[Decorator]` `[Decorate]` `[Intercept]` | Wrap a service, or one you do not own |
| A `static` method carrying a service attribute | Factory, for types the container cannot build |

Full details for each, with the rules and the edge cases, are in the
[documentation](https://ipjohnson.github.io/DependencyModules/).

## Samples

The [`integ-tests/`](https://github.com/ipjohnson/DependencyModules/tree/main/integ-tests)
directory is a working sample gallery, built and tested on every commit:

| Sample | Shows |
|---|---|
| [`SutProject`](https://github.com/ipjohnson/DependencyModules/tree/main/integ-tests/SutProject) | Every registration shape, in one project |
| [`SutProject.Tests`](https://github.com/ipjohnson/DependencyModules/tree/main/integ-tests/SutProject.Tests) | Conventions, realms, keyed services, cross-wiring, factories, features, and all three mocking libraries |
| [`ConsoleTestProject`](https://github.com/ipjohnson/DependencyModules/tree/main/integ-tests/ConsoleTestProject) | Top-level statements and the generated `ApplicationModule` |
| [`web/WebApiApp`](https://github.com/ipjohnson/DependencyModules/tree/main/integ-tests/web/WebApiApp) | An ASP.NET Core host, with its own test project |

## Reporting a problem

When a registration is missing or wrong, three steps produce almost everything needed to
diagnose it:

1. **Read the generated code.** Set `<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>`
   and look under `obj/`. The registrations the generator produced are the ground truth.
   (Point `CompilerGeneratedFilesOutputPath` inside `obj/` — a folder in the project directory
   gets compiled as ordinary source on the next build.)
2. **Turn on the generator log**, which records the configuration in effect, every module and
   service discovered, and anything skipped along with the reason:
   ```xml
   <PropertyGroup>
     <DependencyModules_LogOutputDirectory>$(MSBuildProjectDirectory)/dmlogs</DependencyModules_LogOutputDirectory>
   </PropertyGroup>
   ```
3. **Check for `DM####` warnings** in the build output. The generator reports these for mistakes
   it can detect — see the
   [diagnostics reference](https://ipjohnson.github.io/DependencyModules/reference/diagnostics).

Please include the log and the generated file in any
[issue](https://github.com/ipjohnson/DependencyModules/issues).

## License

MIT. See [LICENSE.txt](https://github.com/ipjohnson/DependencyModules/blob/main/LICENSE.txt)
and [CHANGELOG.md](https://github.com/ipjohnson/DependencyModules/blob/main/CHANGELOG.md).
