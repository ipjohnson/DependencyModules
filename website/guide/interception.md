# Interception

An interceptor is a class that runs code before and after the calls to a service. You write the interceptor one time and use it for many services. For each intercepted service, the generator writes a wrapper class. The wrapper calls the interceptors. The last interceptor calls the service.

## Write an interceptor

An interceptor implements one or more interfaces from the `DependencyModules.Runtime.Interception` namespace:

| Interface | Members that it intercepts |
| --- | --- |
| `IInterceptor` | Methods that have a return value, and `void` methods. Properties, indexers, and events. |
| `IAsyncInterceptor` | Methods that have the return type `Task`, `Task<T>`, `ValueTask`, or `ValueTask<T>`. |
| `IAsyncEnumerableInterceptor` | Methods that have the return type `IAsyncEnumerable<T>`. |

```csharp
using System.Diagnostics;
using DependencyModules.Runtime.Interception;

namespace Inventory;

public class TimingInterceptor : IInterceptor, IAsyncInterceptor
{
    public TResult Intercept<TResult>(InvocationContext<TResult> context)
    {
        var watch = Stopwatch.StartNew();
        var result = context.Proceed();
        Console.WriteLine($"{context.Caller}: {watch.ElapsedMilliseconds} ms");
        return result;
    }

    public async ValueTask<TResult> InterceptAsync<TResult>(
        AsyncInvocationContext<TResult> context
    )
    {
        var watch = Stopwatch.StartNew();
        var result = await context.ProceedAsync();
        Console.WriteLine($"{context.Caller}: {watch.ElapsedMilliseconds} ms");
        return result;
    }
}
```

Each context has these members:

| Member | Function |
| --- | --- |
| `Caller` | A `CallerInfo` value with the service type and the member name. Its `ToString()` gives `Service.Member`. |
| `Arguments` | The arguments of the call. You can read them with `Count`, the indexer, and `NameAt(index)`. You can change an argument before the call continues. |
| `Proceed()` | Calls the next interceptor, or the service after the last interceptor. `AsyncInvocationContext<TResult>` has `ProceedAsync()`. |

For a member that has no return value, `TResult` is the `NoResult` type. These members include `void` methods, `Task` methods, `ValueTask` methods, property `set` accessors, and event accessors.

An interceptor must call `Proceed()` or `ProceedAsync()` to call the service. If the interceptor does not call `Proceed()` or `ProceedAsync()`, the service does not run.

An interceptor can call `Proceed()` or `ProceedAsync()` more than one time, for example to try a call again. Each call runs the subsequent interceptors and the service again.

An async interceptor waits for `ProceedAsync()`. Thus the code after the `await` runs when the call is complete. An `IAsyncEnumerableInterceptor` gets the stream from `Proceed()` and enumerates it. Thus it gets each item of the stream.

## Use interceptors on a class

Put `[Intercept]` on the implementation class. The class must also have a registration, from a service attribute or from a convention.

```csharp
using DependencyModules.Runtime.Attributes;

namespace Inventory;

public interface IStockService
{
    int Count(string sku);

    Task ReserveAsync(string sku, int quantity);
}

[SingletonService]
[Intercept(typeof(TimingInterceptor))]
public class StockService : IStockService
{
    public int Count(string sku) => 10;

    public Task ReserveAsync(string sku, int quantity) => Task.CompletedTask;
}
```

You can give more than one interceptor, for example `[Intercept(typeof(AuditInterceptor), typeof(TimingInterceptor))]`. The first interceptor in the list gets the call first.

You can also put more than one `[Intercept]` attribute on the class. The generator then adds the interceptors in the sequence of the attributes. For `Service`, `Order`, and `Realm`, the generator uses the value from the last attribute that sets the property. The wrapper intercepts only the members that all the attributes select.

The wrapper intercepts only the calls on the service type. If the class calls one of its members, the wrapper does not intercept this call.

## Service type

The generator intercepts one interface of the class. By default, it uses the interface in the class declaration. If the class declaration contains no interface, the generator examines the base classes in sequence. It uses the interfaces of the first base class that declares interfaces.

If the class declares more than one interface, set `Service`:

```csharp
[SingletonService]
[Intercept(typeof(TimingInterceptor), Service = typeof(IStockService))]
public class AuditedStockService : IStockService, IDisposable
{
    public int Count(string sku) => 10;

    public Task ReserveAsync(string sku, int quantity) => Task.CompletedTask;

    public void Dispose() { }
}
```

The generator gives the warning DM0008 in these conditions:

- The generator finds no interface.
- The generator finds more than one interface, and `[Intercept]` does not set `Service`.
- `Service` is an interface that the class does not implement.
- The service type has no members.

The interception is applicable only to the registration of the class. The wrapper does not change a registration of a different implementation type. It changes a registration of the service type that has no known implementation type, for example an instance registration or a factory registration.

## Intercepted members

By default, the wrapper intercepts all members of the service type: methods, properties, indexers, and events. It also intercepts the members of the interfaces that the service type derives from. The `Members` property selects the types of members to intercept:

```csharp
[SingletonService]
[Intercept(typeof(TimingInterceptor), Members = InterceptedMembers.Methods)]
public class MethodsOnlyStockService : IStockService
{
    public int Count(string sku) => 10;

    public Task ReserveAsync(string sku, int quantity) => Task.CompletedTask;
}
```

The values of `InterceptedMembers` are `Methods`, `Properties`, `Indexers`, `Events`, and `All`. You can use more than one value with the `|` operator. The generator reads the text of the `Members` value. Write the values of `InterceptedMembers` directly. If you use a constant, the wrapper intercepts all members. The wrapper sends the calls to the other members directly to the service.

An interceptor intercepts only the members that its interfaces can intercept. For example, an interceptor that implements only `IInterceptor` does not intercept a method that has the return type `Task`. The generator then gives the warning DM0015 with the names of these members.

## Interceptor registration

The generator registers each interceptor as its class type, with `TryAdd`. The `Lifetime` property of `[Intercept]` sets the lifetime of this registration. The default is `Singleton`. The interceptor gets its constructor parameters from the service provider.

If the service collection has a registration for the interceptor class type, the generator does not add a registration. For example, `[SingletonService(As = typeof(TimingInterceptor))]` on the interceptor makes such a registration. A service attribute without `As` registers the interceptor as its first interface, for example `IInterceptor`. That registration does not prevent the `TryAdd` registration.

## Sequence with decorators

The interception occurs at the same time as the decorators, after all modules add their services. The `Order` property of `[Intercept]` sets the position of the interception in the decorator sequence. Write `Order` as a number, for example `Order = 1000`. If you use a constant or `1_000`, the `Order` value is 0. For more information, refer to [Decorator sequence](./decorators.md#decorator-sequence).

## Realms

By default, the interception is applicable in the same modules as the registration of the class. If the service attribute sets `Realm`, the interception uses the same realm. To set a different realm, set `Realm` on `[Intercept]`.

If no module uses the interception, the generator gives the warning DM0020. This can occur when a realm-only module registers the class from a convention and the interception has no realm.

## Generic services

The generator can intercept a generic class. It writes a generic wrapper with the same type parameters and constraints. The generated code also registers the class as its open generic type, with the same lifetime. The wrapper gets the service from this registration.

## Members that the generator cannot intercept

The generator does not intercept a service type that has one of these members:

- A static member
- A method or a property that has a `ref` return value
- A parameter with `ref`, `out`, or `in`
- A parameter or a property of a `ref struct` type
- A property with an `init` accessor
- An event without `add` and `remove` accessors

In these conditions, the generator gives the warning DM0008 and writes no wrapper. The diagnostic gives the name of one member. Other members can have the same problem. If you move these members to a different interface, the generator can intercept the service type. You can also use a [decorator](./decorators.md).

The generator does not give DM0008 for a method that has a `ref struct` return type. For such a method, the generated wrapper does not compile.

## Generated code

For each intercepted class, the generator writes an `internal` wrapper class in the namespace of the class. The name of the wrapper is the class name and the suffix `_Intercepted`, for example `StockService_Intercepted`. For a class that is not generic, the generated code makes the wrapper with a constructor call. Thus it does not use reflection.

When you get an intercepted service from the service provider, you get the wrapper. Thus a test that examines the type of the service finds the wrapper type, not the class type.
