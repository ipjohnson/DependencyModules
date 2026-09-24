# Environments

Registrations that have conditions occur only when the environment agrees with the conditions. For example, a service can register only in the `Development` environment. The environment is an `IModuleEnvironment` from the `DependencyModules.Runtime.Interfaces` namespace:

```csharp
public interface IModuleEnvironment
{
    string EnvironmentName { get; }

    string? Value(string name);
}
```

`EnvironmentName` is the name of the environment. `Value` gives the value for a key. If the environment has no value for the key, `Value` gives `null`.

## The default environment

If you do not give an environment, the modules use the environment of the process:

- The name is the value of the `ASPNETCORE_ENVIRONMENT` environment variable.
- If that variable is not set, the name is the value of the `DOTNET_ENVIRONMENT` environment variable.
- If the two variables are not set, the name is `Production`.
- `Value(name)` gives the value of the environment variable `name`.

`ModuleEnvironment.CreateDefault()` gives a new instance of this environment each time that you call it.

## Give an environment

To give an environment, use the `AddModules` method with an environment parameter:

```csharp
using DependencyModules.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Mail;

var environment = new ModuleEnvironment(
    "Staging",
    new Dictionary<string, string?> { ["FEATURE_BILLING"] = "on" }
);

var services = new ServiceCollection();

services.AddModules(environment, new MailModule());
```

You can also register an `IModuleEnvironment` instance as a singleton before you load the modules. `AddModules` then uses this instance.

`AddModules` uses one environment for all modules of the call. The service collection then contains this environment as `IModuleEnvironment`:

- If you give an environment, `AddModules` removes all `IModuleEnvironment` registrations and registers your environment.
- If you do not give an environment and the service collection has no `IModuleEnvironment`, `AddModules` registers the default environment.

Thus your services can get `IModuleEnvironment` in their constructors. To use values from two environments, get the environment from the service collection first. Then add its values to the new environment.

If you do not give an environment, `AddModules` uses the last `IModuleEnvironment` registration in the service collection. If this registration is not a singleton instance, `AddModules` throws an `InvalidOperationException`. The environment must be available before the service provider is available, because the registrations that have conditions use the environment.

## The `ModuleEnvironment` class

`ModuleEnvironment` in the `DependencyModules.Runtime` namespace implements `IModuleEnvironment`:

| Member | Function |
| --- | --- |
| `new ModuleEnvironment(name, values)` | Makes an environment with a name and optional values. If a key is not in the values, `Value` reads the environment variable. |
| `new ModuleEnvironment(false, name, values)` | Makes an environment that does not read environment variables. |
| `Add(key, value)` | Adds a value. If the environment has a value for the key, `Add` replaces it. |
| `ModuleEnvironment.CreateDefault()` | Gives the environment of the process. |
| `ModuleEnvironment.None` | An environment with an empty name and no values. |

This list gives more information about `ModuleEnvironment`:

- If you give a `Dictionary` for the values, the environment makes a copy with the same `Comparer`. For example, `StringComparer.OrdinalIgnoreCase` makes the keys not case-sensitive.
- If a key has a `null` value, `Value` gives `null` for this key. It does not read the environment variable of that name.
- The environment keeps the value of an environment variable after the first read. If the variable changes, `Value` continues to give the first value. This is also true for the default environment.
- When you enumerate a `ModuleEnvironment`, you get only the values that you gave. You do not get environment variables.
- For `ModuleEnvironment.None`, the `[IfNotEnvironment]` and `[IfNotEnvironmentValue]` conditions are true.

`ModuleEnvironment` is also an `IEnumerable<KeyValuePair<string, string?>>`. Thus you can use a collection initializer to give the values:

```csharp
using DependencyModules.Runtime;

var environment = new ModuleEnvironment(false, "Test")
{
    { "Region", "eu-west-1" },
    { "FEATURE_BILLING", "on" },
};
```

## Registrations with conditions

Put one or more of these attributes on a service class. The attributes are in the `DependencyModules.Runtime.Attributes` namespace.

| Attribute | The service registers when |
| --- | --- |
| `[IfEnvironment("Development", "Staging")]` | The environment name is one of the names. |
| `[IfNotEnvironment("Production")]` | The environment name is not one of the names. |
| `[IfEnvironmentValue("FEATURE_BILLING")]` | The environment has a value for the key. |
| `[IfEnvironmentValue("FEATURE_BILLING", "on")]` | The value for the key is equal to the given value. |
| `[IfNotEnvironmentValue("FEATURE_BILLING")]` | The environment has no value for the key. |
| `[IfNotEnvironmentValue("FEATURE_BILLING", "on")]` | The value for the key is not equal to the given value. |

```csharp
using DependencyModules.Runtime.Attributes;

namespace Mail;

public interface IMailSender
{
    void Send(string to, string body);
}

[SingletonService]
public class SmtpMailSender : IMailSender
{
    public void Send(string to, string body) { }
}

[SingletonService]
[IfEnvironment("Development", "Staging")]
public class FakeMailSender : IMailSender
{
    public void Send(string to, string body) => Console.WriteLine($"{to}: {body}");
}

[DependencyModule]
public partial class MailModule;
```

In the `Development` and `Staging` environments, the module registers `SmtpMailSender` and then `FakeMailSender`. Thus `GetService<IMailSender>()` gives `FakeMailSender`. In the other environments, it gives `SmtpMailSender`. The generator writes the registrations with conditions after the registrations without conditions.

This list gives information about the conditions:

- Environment names are not case-sensitive.
- Values are case-sensitive.
- If a class has more than one condition, all conditions must be true.
- The arguments must be string literals or string constants. The generator ignores an argument that is not a string constant, and it uses the other arguments. For example, it reads `[IfEnvironmentValue("K", null)]` as `[IfEnvironmentValue("K")]`. If no string argument is left, the generator gives the warning DM0012 and ignores the condition.

You can put `[IfEnvironmentValue]` and `[IfNotEnvironmentValue]` on a class more than one time. You can put `[IfEnvironment]` and `[IfNotEnvironment]` on a class only one time.

The generator writes an `if` statement around the registrations of the class. The module then examines the environment when it loads.

## Conditions on decorators and conventions

You can put the same attributes on a `[Decorator]` class. The decorator then changes the registrations only when the conditions are true. The generator also reads these attributes on a decorator that `[Decorate]` adds. For more information, refer to [Decorators](./decorators.md#environment-conditions).

A convention can also have conditions. The conditions of a class that a convention selects are also applicable, also for a class from a referenced assembly. For more information, refer to [Conventions](./conventions.md#environment-conditions).

## Read the environment in a module

A module can implement `IEnvironmentServiceCollectionConfiguration` to read the environment when it loads:

```csharp
using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Mail;

public record MailOptions(string Region);

[DependencyModule(OnlyRealm = true)]
public partial class MailOptionsModule : IEnvironmentServiceCollectionConfiguration
{
    public void ConfigureServices(IServiceCollection services, IModuleEnvironment environment)
    {
        services.AddSingleton(new MailOptions(environment.Value("Region") ?? "default"));
    }
}
```

## Diagnostics

| ID | Severity | Cause |
| --- | --- | --- |
| DM0011 | Info | A service has conditions. The message shows the conditions. |
| DM0012 | Warning | A condition has no environment name or no key. The generator ignores this condition. |

The generator gives DM0011 only for classes with a service attribute. It gives DM0012 also for a `[Decorator]` class, for a decorator that `[Decorate]` adds, and for a class that a convention selects. For a decorator that `[Decorate]` adds, DM0012 is at the module. For a class from a referenced assembly, DM0012 is at the convention statement. On a convention statement, a condition call without a name or a key gives the error DM0009.

## Environments in tests

The test packages can give a different environment to each test. For more information, refer to [Environment for a test](./testing.md#environment-for-a-test).
