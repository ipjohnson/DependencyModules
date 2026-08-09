# Scanning a package

A convention normally matches types in the project being built. `InAssemblyOf<T>()` points it at a
**referenced assembly** instead — for registering handlers, validators or policies out of a package
you do not control.

```csharp
conventions.RegisterAll(typeof(IHandler<,>))
    .InAssemblyOf<SomeTypeInThatPackage>()
    .AsScoped();
```

## It still happens at compile time

The types are read as Roslyn symbols during the build, and each match is emitted as a literal
`typeof()` into your assembly:

```csharp
services.AddScoped(typeof(IHandler<CreateOrder, OrderId>), typeof(ThePackage.CreateOrderHandler));
```

Nothing is loaded or reflected over at run time. This is the point at which reflection-based
scanners stop working under trimming and this one keeps going — see
[Trimming and AOT](/guide/aot).

## The assembly is always named

There is no "scan everything I depend on", and there should not be. Walking every reference visits
thousands of types on every keystroke where one named assembly visits its own — measured at roughly
700× the cost on a minimal eleven-reference compilation.

Naming it with a **type** rather than a string means an assembly that is not referenced cannot be
asked for. The mistake is unexpressible rather than diagnosable.

## What is visible

Only `public` types cross an assembly boundary. A scan of the project being built also sees
`internal` ones.

Nothing can report the type it cannot see, so this is a difference to know rather than one the
generator can warn about.

Types carrying `[SingletonService]` and friends are skipped, as they are in the project being built.
An assembly whose types carry those attributes has its own module and registers them itself —
compose that module instead of scanning it.

## Filters and shapes still apply

```csharp
conventions.RegisterAll<IPolicy>()
    .InAssemblyOf<FirstPolicy>()
    .WithName("Retry*")
    .AsSelf()
    .AsSingleton();
```

## What stays out

Scrutor's `FromApplicationDependencies()`, `FromDependencyContext()` and
`FromAssemblyDependencies(Assembly)` load runtime libraries by name, including assemblies that were
never compile-time references. There is no compile-time answer to that and no way to fake one, so it
is not offered.

Note also that a referenced project **you own** is better served by giving it its own module with
its own conventions and composing through module attributes — explicit, ordered, and cross-assembly
by construction. Scanning earns its keep on assemblies you cannot add a module to.

## Diagnostics

A match from a referenced assembly has no source to point at, so
[DM0010](/reference/diagnostics#dm0010) and friends report at the `RegisterAll` line instead of at
the class.
