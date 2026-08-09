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

Conditions of **different kinds** combine with **and**. Alternatives go inside one attribute, which
is why `IfEnvironment` takes `params` and is not `AllowMultiple` — two of them could never both hold.

```csharp
[SingletonService]
[IfNotEnvironment("Production")]
[IfEnvironmentValue("FEATURE_PROFILING", "on")]
public class RequestProfiler : IProfiler { }
```

Environment **names** compare case-insensitively, matching `IHostEnvironment.IsDevelopment()`.
**Values** compare ordinally, because a value is data rather than a well-known label.

## Where the environment comes from

There is always one, and it is never null.

```csharp
services.AddModules(new ModuleEnvironment("Development"), new ApplicationModule());
```

Supply nothing and you get `ModuleEnvironment.Default`, which reads the process:
`ASPNETCORE_ENVIRONMENT`, then `DOTNET_ENVIRONMENT`, then `"Production"`. Values come from
environment variables, read on each call rather than captured.

That means `[IfEnvironment("Development")]` works with nothing wired up beyond the variable you
already set, and the `"Production"` default means a service gated on a non-production environment
stays unregistered unless something says otherwise.

`ModuleEnvironment.None` says this application has no environment — a real object with an empty name
and no values, rather than a null to branch on.

Whatever is used is **registered**, so `GetRequiredService<IModuleEnvironment>()` returns the same
environment that decided the registrations.

::: warning Register an instance, not a type
The environment is read while the collection is still being populated, before any provider exists,
so only a singleton **instance** can be used.

```csharp
services.AddSingleton<IModuleEnvironment>(new ModuleEnvironment("Staging"));   // works
services.AddSingleton<IModuleEnvironment, MyEnvironment>();                    // throws
```

Registering by type or factory is refused with a message naming the fix, rather than silently
falling back to the process default.
:::

An environment passed to `AddModules` **replaces** one already in the collection rather than joining
it. The environment answers a single question and several would need a rule for which one wins. To
layer one on another, read the existing one and combine before you call:

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

In Development the fake wins, because the container resolves a single service from the last matching
descriptor.

Across modules, **module order decides** — a referenced module's conditional registration does not
override the module that references it. A condition says "instead of my other registration", not
"instead of yours".

::: info Try is first-wins
`Using(RegistrationType.Try)` is first-wins rather than last-wins, so a conditional `Try` cannot
override an unconditional one. The override pattern wants `Add`, which is the default.
:::

## Conditions and conventions

A class matched by a convention honours its conditions too. A class carrying a service attribute is
never a convention candidate, so a condition on a convention-matched class has no other route — and
dropping it would put a development-only service into production.

## What conditions cost

The test runs at run time, so **both branches are compiled and every conditionally registered type
stays referenced**. Conditions change what is registered, not what ships. Trimming a service out of a
build is a compile-time decision and belongs to `#if`.

## Seeing it at build time

Whether a condition holds is a run-time question, so there is no build error for the ordinary case.
[DM0011](/reference/diagnostics#dm0011) reports what each conditional registration depends on, so
the condition is visible where you are already looking.

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
