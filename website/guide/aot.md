# Trimming and Native AOT

## The problem

You publish trimmed, or as Native AOT, and the application dies at startup:

```
System.InvalidOperationException: Unable to resolve service for type 'MyApp.IHandler'
```

Nothing changed in your code, and it works perfectly in development. This is the classic failure of
runtime assembly scanning, and it is worth understanding why it happens rather than which flag
suppresses it.

A reflection-based scanner enumerates an assembly's types when the application starts. The trimmer
runs long before that, and its job is to remove any type nothing references. It has no way to know
your scanner will go looking for `CreateOrderHandler`, because nothing in your code mentions
`CreateOrderHandler` — that is the whole appeal of scanning. So the trimmer removes it, the scan
finds nothing, and the container has no registration.

The failure only appears in a published build, which is the worst place to discover it.

## How DependencyModules helps

The same work happens during the build instead, and each match is emitted as a literal `typeof()`
into your assembly:

```csharp
services.AddScoped(typeof(IHandler<CreateOrder, OrderId>), typeof(CreateOrderHandler));
```

Two things follow from that one line, and together they are the whole story.

**The trimmer roots the type.** A `typeof()` in your code is an ordinary static reference — exactly
the thing the trimmer is looking for. There is nothing dynamic to see through.

**The constructor survives too.** `ServiceDescriptor`'s implementation-type parameter carries
`[DynamicallyAccessedMembers(PublicConstructors)]`, and that annotation can only flow to a type the
compiler knows about. Because the type is named literally, it does.

Both hold for [types in a referenced package](/guide/scanning) as well, which is the case runtime
scanners handle worst.

## What this covers

- Attribute registration
- Conventions, including open generics and referenced-assembly scanning
- Decorators and interception — the wrapper is generated code in your own assembly

## What it does not cover

**Environment conditions decide behaviour, not size.** The test runs at run time, so both branches
compile and every conditionally registered type stays referenced. Removing a service from a build is
a compile-time decision, and belongs to `#if`. See
[what conditions cost](/guide/environments#what-conditions-cost).

**Open generic registration is the least AOT-friendly part of the container itself**, independent of
this library — the container has to construct a closed type at run time, and Native AOT only has code
for the instantiations the compiler could see.

In practice the line falls between reference and value type arguments. Measured on a published
`osx-arm64` binary, with `[SingletonService]` on `Bin<T> : IBin<T>`:

```
GetRequiredService<IBin<string>>()   works — reference types share one instantiation
GetRequiredService<IBin<int>>()      InvalidOperationException: Unable to create a generic service
                                     for type 'IBin`1[System.Int32]' because 'System.Int32' is a
                                     ValueType. Native code to support creating generic services
                                     might not be available with native AOT.
```

This is the container, not the generator: an [intercepted](/guide/interception) open generic behaves
exactly the same way, because it is registered the same way. If you are targeting Native AOT, register
closed constructions — a [convention](/guide/conventions) over the open generic does that for you,
emitting one registration per implementation.

**Runtime assembly discovery is not supported**, because there would be nothing to resolve at build
time. See [Scanning a package](/guide/scanning).

## The generator never ships

Worth stating plainly, since "source generator" sometimes reads as "extra thing in my output".

The analyzer packages contain no `lib/` folder, so they cannot reach your build output at all, and
`DevelopmentDependency=true` stops them flowing transitively to anything referencing your library.

Only `DependencyModules.Runtime` is a run-time dependency, and it holds interfaces, attributes and a
small registry — no Roslyn, and no reflection over your types.
