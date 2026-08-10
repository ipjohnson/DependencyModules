# Environments

## The problem

You do not want your development machine sending real email. So the registration becomes conditional:

```csharp
// Program.cs
if (builder.Environment.IsDevelopment()) {
    services.AddSingleton<IEmailSender, FakeEmailSender>();
} else {
    services.AddSingleton<IEmailSender, SmtpEmailSender>();
}
```

This works, and it has a habit of multiplying. The decision lives in `Program.cs`, a long way from
either class, so reading `FakeEmailSender` tells you nothing about when it is used. After a few of
these the composition root is a pile of branches, and the only way to know what runs in staging is to
trace all of them.

## How DependencyModules helps

Put the condition on the class, next to the registration it qualifies:

```csharp
[SingletonService]
[IfEnvironment("Development", "Staging")]
public class FakeEmailSender : IEmailSender { }

[SingletonService]
[IfNotEnvironment("Development")]
public class SmtpEmailSender : IEmailSender { }
```

Resolve `IEmailSender` and you get whichever one the environment selected. `Program.cs` has no branch
in it, and each class states its own applicability where you will actually read it.

## The conditions

| Attribute | Registers when |
|---|---|
| `[IfEnvironment(params string[])]` | the environment name matches any of them |
| `[IfNotEnvironment(params string[])]` | it matches none of them |
| `[IfEnvironmentValue(key)]` | the environment has any value for the key |
| `[IfEnvironmentValue(key, value)]` | the value equals exactly |
| `[IfNotEnvironmentValue(…)]` | the inverse of either form |

Conditions of **different kinds** combine with **and**; alternatives go inside one attribute as
`params`. So this registers only outside production, and only when the feature is switched on:

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

The simplest case needs no wiring at all. Supply nothing and you get `ModuleEnvironment.CreateDefault()`,
which reads the process — `ASPNETCORE_ENVIRONMENT`, then `DOTNET_ENVIRONMENT`, then falling back to
`"Production"`. Values come from environment variables.

So `[IfEnvironment("Development")]` already works against the variable your tooling sets for you.

To decide explicitly — and a test should — pass one to `AddModules`:

```csharp
services.AddModules(new ModuleEnvironment("Development"), new ApplicationModule());
```

Values go inline, since a `ModuleEnvironment` is a collection of them:

```csharp
services.AddModules(
    new ModuleEnvironment("Development") {
        { "FEATURE_PROFILING", "on" },
        { "REGION", "eu" }
    },
    new ApplicationModule());
```

A dictionary still works, and the two combine — an entry written inline replaces one of the same key
that came from the dictionary.

### What happens to keys you did not write

Anything **not** written there falls back to an environment variable of that name, so supplying a
couple of values does not mean giving up the rest.

A key you did write wins — including one written as `null`, which is how you hide a variable of the
same name:

```csharp
new ModuleEnvironment("Development") {
    { "REGION", "eu" },        // wins over any REGION variable
    { "FEATURE_PROFILING", null }   // hides a FEATURE_PROFILING variable
}
```

To pin an environment to exactly what is at the call site and read nothing else, lead with `false`:

```csharp
new ModuleEnvironment(false, "Development") { { "REGION", "eu" } }   // reads nothing else
```

A test asserting which services an environment registers wants this. Otherwise a variable set on the
machine running it can reach a key the test never mentioned, and the test passes or fails depending
on whose machine it runs on.

The flag leads rather than trailing, so it is read before the values it governs. Both forms still
take a dictionary, so turning fallback off does not mean giving up the constructor you were using:

```csharp
new ModuleEnvironment(false, "Development", new Dictionary<string, string?> { ["A"] = "1" })
```

A comparer you supplied on that dictionary is carried over rather than reset — useful for
`OrdinalIgnoreCase`, matching how Windows treats variable names.

### What gets cached

An environment caches what it reads from the process, misses included, for its own lifetime. The
instance `AddModules` registers is held for the application's lifetime, so a service that injects
`IModuleEnvironment` and reads a value per request pays one process lookup rather than one per call —
and an unset optional variable, which is the case a default exists for, is cached as absent rather
than re-read every time.

The trade is that an instance does not see a variable changed mid-process. `CreateDefault()` builds a
fresh one on each call, so asking again is how you get a current view:

```csharp
var current = ModuleEnvironment.CreateDefault().Value("FEATURE_X");
```

Values you supplied yourself are never affected — they are answered directly, and the cache only ever
holds what came from the process. Enumerating a `ModuleEnvironment` still yields only what you
supplied.

### Stating that there is no environment

`ModuleEnvironment.None` has an empty name and no values, so every condition evaluates false. Prefer
it to leaving the environment unset, which silently picks up the process instead.

Whatever is used is **registered**, so `GetRequiredService<IModuleEnvironment>()` afterwards returns
the same environment that decided the registrations.

::: warning Register an instance, not a type
The environment is read while the collection is still being populated — before any provider exists to
resolve anything — so only a singleton **instance** can work.

```csharp
services.AddSingleton<IModuleEnvironment>(new ModuleEnvironment("Staging"));   // works
services.AddSingleton<IModuleEnvironment, MyEnvironment>();                    // throws
```

Registering by type or by factory throws, with a message naming the fix.
:::

An environment passed to `AddModules` **replaces** one already in the collection. To layer one on
another, read the existing one and combine before you call:

```csharp
var existing = services.FirstOrDefault(d => d.ServiceType == typeof(IModuleEnvironment))
    ?.ImplementationInstance as IModuleEnvironment;

services.AddModules(Combine(existing ?? ModuleEnvironment.CreateDefault(), overlay), modules);
```

## Overriding a default

A conditional registration is emitted **after** the unconditional ones in its module, which is what
makes the override pattern work — register the normal implementation unconditionally and the special
one conditionally:

```csharp
[SingletonService]                                   public class SmtpEmailSender : IEmailSender { }
[SingletonService] [IfEnvironment("Development")]    public class FakeEmailSender : IEmailSender { }
```

In Development the fake wins, because the container resolves a single service from the **last**
matching descriptor.

Across modules, **module order decides** — a referenced module's conditional registration does not
override the module that references it.

::: info Try is first-wins
A conditional `Using(RegistrationType.Try)` cannot override an unconditional registration, since
`Try` declines when the service type is already present. Use `Add`, the default, for this pattern.
:::

## Conditions and conventions

A class matched by a [convention](/guide/conventions) honours its own conditions, so an attribute
behaves the same whether the class is registered by attribute or by rule.

A convention can also carry a condition itself, gating every match rather than making you repeat the
attribute on each class:

```csharp
conventions.RegisterAll<IDiagnostic>().IfEnvironment("Development").AsScoped();
```

The same four tests are available, named after the attributes:

| Call | Registers when |
|---|---|
| `IfEnvironment(params string[])` | the environment name matches any of them |
| `IfNotEnvironment(params string[])` | it matches none of them |
| `IfEnvironmentValue(key)` · `IfEnvironmentValue(key, value)` | the key is present, or equals exactly |
| `IfNotEnvironmentValue(…)` | the inverse of either form |

When a convention carries a condition **and** a matched class carries its own, the two combine with
**and** — neither can silently discard the other:

```csharp
conventions.RegisterAll<IFoo>().IfEnvironment("Development").AsSingleton();

[IfEnvironmentValue("REGION", "eu")]
public class EuFoo : IFoo { }

// EuFoo registers only when the environment is Development AND REGION is eu
```

## Conditions and decorators

A [decorator](/guide/decorators#decorating-only-in-some-environments) takes the same conditions. Where
it does not apply, the service resolves undecorated, and the ordering of everything else is
unchanged.

## What conditions cost

The test runs at **run time**, which means both branches are compiled and every conditionally
registered type stays referenced in the output.

Conditions change what is *registered*, not what *ships*. To keep a service out of a build entirely,
you want `#if`.

## Seeing it at build time

[DM0011](/reference/diagnostics#dm0011) reports what each conditional registration depends on, inline
in the IDE at the class — so the applicability is visible without running anything.

A condition that names nothing to test — `[IfEnvironment()]`, `[IfEnvironmentValue("")]` — is
[DM0012](/reference/diagnostics#dm0012). Both compile, and both are almost certainly a mistake.

## Programmatic access

For registration that depends on the environment but is not a simple condition:

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
