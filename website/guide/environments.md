# Environments

A registration can depend on the environment the application is running in.

```csharp
[SingletonService]
[IfEnvironment("Development", "Staging")]
public class FakeEmailSender : IEmailSender { }

[SingletonService]
[IfNotEnvironment("Development")]
public class SmtpEmailSender : IEmailSender { }
```

Resolve `IEmailSender` and you get whichever one the environment selected.

## The attributes

| Attribute | Registers when |
|---|---|
| `[IfEnvironment(params string[])]` | the environment name matches any of them |
| `[IfNotEnvironment(params string[])]` | it matches none of them |
| `[IfEnvironmentValue(key)]` | the environment has any value for the key |
| `[IfEnvironmentValue(key, value)]` | the value equals exactly |
| `[IfNotEnvironmentValue(…)]` | the inverse of either form |

Conditions of **different kinds** combine with **and**. Alternatives go inside one attribute, as
`params`.

```csharp
[SingletonService]
[IfNotEnvironment("Production")]
[IfEnvironmentValue("FEATURE_PROFILING", "on")]
public class RequestProfiler : IProfiler { }
```

Environment **names** compare case-insensitively, matching `IHostEnvironment.IsDevelopment()`.
**Values** compare ordinally.

## Where the environment comes from

There is always one, and it is never null.

```csharp
services.AddModules(new ModuleEnvironment("Development"), new ApplicationModule());
```

Supply nothing and you get `ModuleEnvironment.Default`, which reads the process:
`ASPNETCORE_ENVIRONMENT`, then `DOTNET_ENVIRONMENT`, then `"Production"`. Values come from
environment variables, read on each call rather than captured.

So `[IfEnvironment("Development")]` works with nothing wired up beyond the variable you already set.

`ModuleEnvironment.None` says this application has no environment — an empty name and no values.

Whatever is used is **registered**, so `GetRequiredService<IModuleEnvironment>()` returns the same
environment that decided the registrations.

::: warning Register an instance, not a type
The environment is read while the collection is still being populated, before any provider exists,
so only a singleton **instance** can be used.

```csharp
services.AddSingleton<IModuleEnvironment>(new ModuleEnvironment("Staging"));   // works
services.AddSingleton<IModuleEnvironment, MyEnvironment>();                    // throws
```

Registering by type or factory throws, with a message naming the fix.
:::

An environment passed to `AddModules` **replaces** one already in the collection. To layer one on
another, read the existing one and combine before you call:

```csharp
var existing = services.FirstOrDefault(d => d.ServiceType == typeof(IModuleEnvironment))
    ?.ImplementationInstance as IModuleEnvironment;

services.AddModules(Combine(existing ?? ModuleEnvironment.Default, overlay), modules);
```

## Ordering

A conditional registration is emitted **after** the unconditional ones in its module, so it can
override a default:

```csharp
[SingletonService]                                  public class SmtpEmailSender : IEmailSender { }
[SingletonService] [IfEnvironment("Development")]    public class FakeEmailSender : IEmailSender { }
```

In Development the fake wins, since the container resolves a single service from the last matching
descriptor.

Across modules, **module order decides** — a referenced module's conditional registration does not
override the module that references it.

::: info Try is first-wins
A conditional `Using(RegistrationType.Try)` cannot override an unconditional registration. Use `Add`,
the default, for the override pattern.
:::

## Conditions and conventions

A class matched by a [convention](/guide/conventions) honours its conditions too, so a condition
works whether the class is registered by attribute or by convention.

## What conditions cost

The test runs at run time, so **both branches are compiled and every conditionally registered type
stays referenced**. Conditions change what is registered, not what ships. To remove a service from a
build, use `#if`.

## Seeing it at build time

[DM0011](/reference/diagnostics#dm0011) reports what each conditional registration depends on, in the
IDE at the class.

A condition that names nothing to test — `[IfEnvironment()]`, `[IfEnvironmentValue("")]` — is
[DM0012](/reference/diagnostics#dm0012).

## Programmatic access

For registration that needs the environment but is not a simple condition:

```csharp
[DependencyModule]
public partial class ApplicationModule : IEnvironmentServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services, IModuleEnvironment environment) {
        if (environment.Value("REGION") == "eu") {
            services.AddSingleton<IStorage, EuStorage>();
        }
    }
}
```

It receives the same non-null environment the attributes are evaluated against.
