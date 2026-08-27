# Runtime interfaces

`DependencyModules.Runtime.Interfaces` holds the contracts the generated code implements and calls.
Most projects never name one — the attributes are the surface you write against — but they turn up in
three places: a compiler error naming a type you did not write, a generated file you opened to see
what happened, and the seam you implement when an attribute cannot express something.

They are covered by the [semantic versioning promise](https://semver.org/spec/v2.0.0.html): none of
them breaks within 1.x.

## `IDependencyModule`

What every module is. The generator implements it on the partial class you declared, so you never
write it yourself — but it is the constraint on `AddModule<TModule>()`, which is why it shows up in
an error when a module did not get generated:

```
error CS0311: The type 'ApplicationModule' cannot be used as type parameter 'T' … no implicit
reference conversion from 'ApplicationModule' to 'IDependencyModule'.
```

That message means the generator did not complete your class. The usual causes each have a
diagnostic beside them — the module is not `partial`
([DM0003](/reference/diagnostics#dm0003)), or it is nested inside another type
([DM0017](/reference/diagnostics#dm0017)).

## `IDependencyModuleProvider`

What a module's **generated attribute** implements. `[DataModule]` on another module is an instance
of `DataModuleAttribute`, and this is how the runtime gets a module out of it.

You do not implement this. It is worth knowing because it is what makes a module attribute
recognisable in a referenced package — which is how [DM0016](/reference/diagnostics#dm0016) and
[DM0019](/reference/diagnostics#dm0019) find a module that came from a NuGet package rather than from
your own project.

## `IServiceCollectionConfiguration`

The escape hatch for registration an attribute cannot express — `AddHttpClient()`, options binding,
anything that is a method call rather than a class you own. Implement it on a module and the generator
calls it alongside the registrations it wrote:

```csharp
[DependencyModule]
public partial class ApplicationModule : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        services.AddHttpClient();
        services.Configure<CacheOptions>(options => options.SizeLimit = 1024);
    }
}
```

It is also how you extend a module the generator wrote for you. A project with top-level statements
gets an `ApplicationModule` it did not declare; adding a partial for it **without**
`[DependencyModule]` and implementing this interface merges into that module rather than colliding
with it:

```csharp
public partial class ApplicationModule : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) =>
        services.AddHttpClient();
}
```

See [Modules](/guide/modules#when-attributes-are-not-enough).

## `IModuleEnvironment`

What [`[IfEnvironment]`](/guide/environments) is evaluated against. `ModuleEnvironment` implements it,
and `AddModules` takes one:

```csharp
services.AddModules(new ModuleEnvironment("Development"), new ApplicationModule());
```

Implement it yourself when the environment is not a name from `DOTNET_ENVIRONMENT` — a feature flag
service, or a configuration section.

## Where they live

All of them are in `DependencyModules.Runtime`, which is the package you already reference:

```csharp
using DependencyModules.Runtime.Interfaces;
```

The interception contracts — `IInterceptor`, `IAsyncInterceptor`, `IAsyncEnumerableInterceptor` — are
in `DependencyModules.Runtime.Interception` instead, and are covered in
[Interception](/guide/interception). The convention contracts are in
`DependencyModules.Runtime.Conventions`; see the [Convention API](/reference/conventions-api).
