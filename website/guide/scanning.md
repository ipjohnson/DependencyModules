# Scanning a package

## The problem

A [convention](/guide/conventions) matches types in the project being built. That covers your own
code, but not this:

You depend on a package that ships a dozen `IHandler<,>` implementations and no
`AddThePackage(services)` extension method. You cannot put `[TransientService]` on those classes —
they are not yours — and a convention declared in your project does not look inside them.

## How DependencyModules helps

`InAssemblyOf<T>()` points a convention at a **referenced assembly**, using any type from it as the
marker:

```csharp
conventions.RegisterAll(typeof(IHandler<,>))
    .InAssemblyOf<SomeTypeInThatPackage>()
    .AsScoped();
```

The package's types are read during **your** build, and each match is emitted as a literal `typeof()`
into **your** assembly:

```csharp
// generated, in your project
services.AddScoped(typeof(IHandler<CreateOrder, OrderId>), typeof(ThePackage.CreateOrderHandler));
```

Nothing is loaded or reflected over at run time, so this survives trimming exactly as your own
registrations do — see [Trimming and AOT](/guide/aot).

## Filters and shapes work the same

Everything from [Conventions](/guide/conventions#narrowing-what-matches) applies:

```csharp
conventions.RegisterAll<IPolicy>()
    .InAssemblyOf<FirstPolicy>()
    .WithName("Retry*")
    .AsSelf()
    .AsSingleton();
```

## What you can and cannot see

**Only `public` types cross an assembly boundary.** A scan of your own project also sees `internal`
types; a scan of a package does not. Nothing warns about this — the generator cannot report on what
it cannot see — so a convention that matches less than you expected is usually this.

**One assembly at a time.** There is no "scan everything I depend on". Point each convention at the
assembly you mean.

**Attributed types are skipped**, just as they are in your own project. An assembly whose types carry
`[SingletonService]` and friends already has its own module — compose that module instead of scanning
it, and you get the author's intended lifetimes rather than your guess at them.

## When not to reach for it

Scanning is for assemblies you **do not own**.

For a project you do own, give it its own module with its own conventions and compose through
[module attributes](/guide/modules#composing-modules). That already works across assemblies, and it
keeps each project in charge of its own registrations rather than making the consumer guess at them.

Discovering assemblies at run time is not supported at all, since there would be nothing to resolve
at build time.

## Diagnostics

A match from a referenced assembly has no source location to point at, so
[DM0010](/reference/diagnostics#dm0010) and friends report at the `RegisterAll` line instead of at
the class.
