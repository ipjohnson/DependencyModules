# Trimming and Native AOT

What survives trimming, and what does not.

## The problem with scanning at run time

A reflection-based scanner enumerates an assembly's types when the application starts. The trimmer
cannot follow that: it has no way to know those types are needed, so it removes them, and the scan
finds nothing. The failure appears at startup in a published build and never in development.

## What happens here instead

The same work happens during the build, and each match is emitted as a literal `typeof()` into your
assembly:

```csharp
services.AddScoped(typeof(IHandler<CreateOrder, OrderId>), typeof(CreateOrderHandler));
```

Two things follow.

**The trimmer roots the type**, because a `typeof()` in your code is an ordinary static reference.

**The constructor survives too.** `ServiceDescriptor`'s implementation-type parameter carries
`[DynamicallyAccessedMembers(PublicConstructors)]`, and that annotation can only flow to a type the
compiler knows about.

Both hold for [types in a referenced package](/guide/scanning) as well.

## What this covers

- Attribute registration
- Conventions, including open generics and referenced-assembly scanning
- Decorators and interception — the wrapper is generated code in your assembly

## What it does not cover

**Environment conditions decide behaviour, not size.** The test runs at run time, so both branches
are compiled and every conditionally registered type stays referenced. Removing a service from a
build is a compile-time decision and belongs to `#if`.

**Open generic registration is the least AOT-friendly part of the container itself**, independent of
this library. If you are targeting Native AOT aggressively, prefer closed registrations.

**Runtime assembly discovery is not supported**, since there is nothing to resolve at build time. See
[Scanning a package](/guide/scanning).

## The generator never ships

The analyzer packages contain no `lib/` folder, so they cannot reach your output, and
`DevelopmentDependency=true` stops them flowing transitively to anything that references your
library. Only `DependencyModules.Runtime` is a run-time dependency, and it holds interfaces,
attributes and a small registry — no Roslyn, no reflection over your types.
