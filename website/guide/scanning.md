# Scanning a package

A convention normally matches types in the project being built. `InAssemblyOf<T>()` points it at a
**referenced assembly** instead — for registering handlers, validators or policies out of a package
you do not control.

```csharp
conventions.RegisterAll(typeof(IHandler<,>))
    .InAssemblyOf<SomeTypeInThatPackage>()
    .AsScoped();
```

The types are read during the build, and each match is emitted as a literal `typeof()` into your
assembly:

```csharp
services.AddScoped(typeof(IHandler<CreateOrder, OrderId>), typeof(ThePackage.CreateOrderHandler));
```

Nothing is loaded or reflected over at run time, so this survives trimming — see
[Trimming and AOT](/guide/aot).

## Name one assembly at a time

There is no "scan everything I depend on". Point each convention at the assembly you want, using any
type from it as the marker.

## What is visible

Only `public` types cross an assembly boundary, where a scan of your own project also sees `internal`
ones. Nothing warns about this — the generator cannot see what it cannot see.

Types carrying `[SingletonService]` and friends are skipped, as they are in your own project. An
assembly whose types carry those attributes has its own module; compose that module rather than
scanning it.

## Filters and shapes still apply

```csharp
conventions.RegisterAll<IPolicy>()
    .InAssemblyOf<FirstPolicy>()
    .WithName("Retry*")
    .AsSelf()
    .AsSingleton();
```

## When to reach for it

Scanning is for assemblies you **do not own** — a package whose handlers or validators you want
registered.

For a project you own, give it its own module with its own conventions and compose through module
attributes. That works across assemblies already, and keeps each project in charge of its own
registrations.

Discovering assemblies at run time is not supported, since there is nothing to resolve at build time.

## Diagnostics

A match from a referenced assembly has no source to point at, so
[DM0010](/reference/diagnostics#dm0010) and friends report at the `RegisterAll` line.
