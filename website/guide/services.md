# Registering services

Four attributes cover most registration. Each maps onto the `IServiceCollection` call you would
otherwise write.

| Attribute | Emits |
|---|---|
| `[SingletonService]` | `AddSingleton` |
| `[ScopedService]` | `AddScoped` |
| `[TransientService]` | `AddTransient` |
| `[CrossWireService]` | the implementation **and** every interface it declares, sharing one instance |

```csharp
[SingletonService]
public class SmtpEmailSender : IEmailSender { }
```

A class with no interface registers as itself. A class with interfaces registers as the first one it
declares, unless you say otherwise with `As`.

## Choosing the service type

```csharp
[SingletonService(As = typeof(IEmailSender))]
public class SmtpEmailSender : IEmailSender, IDiagnosticSource { }
```

## Cross wiring

`[CrossWireService]` is the "one instance, several front doors" registration. Resolving the concrete
type or any of its interfaces gives the same instance.

```csharp
[CrossWireService]
public class Cache : IReadCache, IWriteCache { }
```

Two independent registrations would give you one instance per service type, which is almost never
what people mean.

## Keys

```csharp
[SingletonService(Key = "primary")]
public class PrimaryConnection : IConnection { }
```

```csharp
provider.GetRequiredKeyedService<IConnection>("primary");
```

The key is written into the registration as you wrote it, so a literal, a `const` or an enum member
all work.

## Registration strategy

`Using` chooses how the registration is added.

| Value | Behaviour |
|---|---|
| `Add` *(default)* | always adds |
| `Try` | adds only if the service type is not already registered |
| `TryEnumerable` | adds unless this exact service/implementation pair is present |
| `Replace` | replaces an existing registration of the service type |

```csharp
[SingletonService(Using = RegistrationType.Try)]
public class DefaultClock : IClock { }
```

## Factories

When a type cannot be constructed by the container, register a static factory method instead. The
attribute goes on the method.

```csharp
public class SomeClass : ISomeInterface {
    public SomeClass(IDep one, IDepTwo two, DateTime timestamp) { }

    [SingletonService]
    public static ISomeInterface Factory(IDep one, IDepTwo two) =>
        new SomeClass(one, two, DateTime.UtcNow);
}
```

Every parameter of the factory is resolved from the container.

## Constructor selection

The greediest accessible constructor is used, matching `ActivatorUtilities`. To pick a specific one,
mark it:

```csharp
[ActivatorUtilitiesConstructor]
public SomeClass(IDep one) { }
```

## Generated factories

By default the generator emits `typeof(Implementation)` and lets the container construct it. Turning
on factory generation emits a `new` expression instead, which removes the container's reflection
from the hot path:

```xml
<PropertyGroup>
  <DependencyModules_GenerateFactories>true</DependencyModules_GenerateFactories>
</PropertyGroup>
```

See [MSBuild properties](/reference/msbuild) for the rest.
