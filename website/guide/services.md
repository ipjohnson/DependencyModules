# Registering services

Each registration line you would have written by hand answers three questions: how long the instance
lives, what type callers ask for, and how the registration is added to the collection. This page
covers how to answer each one with an attribute.

## Lifetime

The attribute name is the lifetime, and it maps directly onto the call it replaces:

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

```csharp
// generated
services.AddSingleton(typeof(IEmailSender), typeof(SmtpEmailSender));
```

## What callers ask for

By default, a class with interfaces registers as **the first interface it declares**, and a class
with no interface registers as itself.

That default is wrong as soon as a class implements two interfaces for different reasons:

```csharp
[SingletonService]
public class SmtpEmailSender : IEmailSender, IDiagnosticSource { }
```

Here `IEmailSender` happens to be first, but nothing about the code says that was deliberate — and
reordering the base list would silently change the registration. Say which one you meant:

```csharp
[SingletonService(As = typeof(IEmailSender))]
public class SmtpEmailSender : IEmailSender, IDiagnosticSource { }
```

Two details of the default worth knowing, since neither is guessable:

**Capability interfaces are passed over.** `IDisposable`, `IEquatable<T>`, `IComparable`,
`INotifyPropertyChanged`, `IEnumerable<T>` and their relatives describe something a class *can do*,
not what callers ask for. They are skipped when picking the default, so this registers as `IPool`
rather than `IDisposable`:

```csharp
[SingletonService]
public class ConnectionPool : IDisposable, IPool { }
```

If a capability interface is the only one, the class registers as itself. This is inference only —
`[SingletonService(As = typeof(IDisposable))]` is always honoured.

Note that framework interfaces which *are* genuine service roles stay eligible, so
`IEqualityComparer<T>`, `IJsonTypeInfoResolver` and `IHttpClientFactory` all work as you would
expect.

**A class declaring no interface of its own inherits the search.** The generator walks up the base
classes looking for one, which is what makes `class OrderRepository : RepositoryBase` register as
`IRepository`, and `class Worker : BackgroundService` register as `IHostedService`. If it finds
nothing but capability interfaces, the class registers as itself.

### One instance behind several interfaces

Sometimes both interfaces are the point. A cache with a read side and a write side wants **one
instance** reachable through either:

```csharp
[CrossWireService]
public class Cache : IReadCache, IWriteCache { }
```

```csharp
provider.GetRequiredService<IReadCache>();    // same instance
provider.GetRequiredService<IWriteCache>();   // as this one
```

Registering the two interfaces separately would give you one instance per service type instead, which
for a cache means two caches and a bug that takes a while to find.

## Several implementations of one interface

When more than one implementation is registered, a key says which one you want:

```csharp
[SingletonService(Key = "primary")]
public class PrimaryConnection : IConnection { }

[SingletonService(Key = "reporting")]
public class ReportingConnection : IConnection { }
```

```csharp
provider.GetRequiredKeyedService<IConnection>("primary");
```

The key is written into the registration exactly as you wrote it, so a string literal, a `const` or
an enum member all work.

## How the registration is added

By default every attribute adds unconditionally, so registering the same service type twice leaves
two descriptors and the last one wins. `Using` changes that:

| Value | Behaviour |
|---|---|
| `Add` *(default)* | always adds |
| `Try` | adds only if the service type is not already registered |
| `TryEnumerable` | adds unless this exact service/implementation pair is present |
| `Replace` | replaces an existing registration of the service type |

`Try` is the one a library wants for a default the application should be able to override:

```csharp
[SingletonService(Using = RegistrationType.Try)]
public class DefaultClock : IClock { }
```

The application registers its own `IClock` and wins; if it does not, `DefaultClock` is there.

## When the container cannot construct the type

Some classes need something the container has no way to supply — a timestamp, a value from
configuration, an object built by a factory somewhere else. Put the attribute on a **static factory
method** instead of on the class:

```csharp
public class SomeClass : ISomeInterface {
    public SomeClass(IDep one, IDepTwo two, DateTime timestamp) { }

    [SingletonService]
    public static ISomeInterface Factory(IDep one, IDepTwo two) =>
        new SomeClass(one, two, DateTime.UtcNow);
}
```

Every parameter of the factory method is resolved from the container; everything else is yours to
supply.

## Choosing a constructor

With several constructors, the greediest accessible one is used — the same rule `ActivatorUtilities`
follows. To pin a specific one:

```csharp
[ActivatorUtilitiesConstructor]
public SomeClass(IDep one) { }
```

## Removing the container's reflection

By default the generator emits `typeof(Implementation)` and lets the container construct it, which it
does by reflection. Turning on factory generation emits a `new` expression instead:

```xml
<PropertyGroup>
  <DependencyModules_GenerateFactories>true</DependencyModules_GenerateFactories>
</PropertyGroup>
```

```csharp
// generated, with the property set
services.AddSingleton(
    typeof(ISummaryProvider),
    provider => new SummaryProvider(provider.GetRequiredService<IAiSummaryProvider>())
);
```

Every constructor dependency becomes an explicit `GetRequiredService` call, so the container never
reflects over the constructor. Worth it when you are chasing startup time or targeting Native AOT
aggressively. See [MSBuild properties](/reference/msbuild) for the rest.

## Next

- [Conventions](/guide/conventions) — when one attribute per class stops scaling
- [Environments](/guide/environments) — registering a different implementation per environment
