# Diagnostics

Each code can be tuned or silenced through `.editorconfig`:

```ini
dotnet_diagnostic.DM0010.severity = none
```

| Code | Severity | Meaning |
|---|---|---|
| [DM0001](#dm0001) | Error | The generator failed |
| [DM0002](#dm0002) | Warning | A service type cannot be constructed |
| [DM0003](#dm0003) | Error | A module is not `partial` |
| [DM0004](#dm0004) | Error | Two conventions register a type as the same service type |
| [DM0005](#dm0005) | Warning | A convention matched nothing |
| [DM0006](#dm0006) | Warning | A convention matched a type with no accessible constructor |
| [DM0007](#dm0007) | Error | Two decorators of one service share an order |
| [DM0008](#dm0008) | Warning | A service marked for interception cannot be wrapped |
| [DM0009](#dm0009) | Error | A convention declaration could not be read |
| [DM0010](#dm0010) | Info | A service is registered by convention |
| [DM0011](#dm0011) | Info | A service is registered only when a condition holds |
| [DM0012](#dm0012) | Warning | An environment condition names nothing to test |

## DM0001 {#dm0001}

**The generator failed; registrations may be missing.**

Please [open an issue](https://github.com/ipjohnson/DependencyModules/issues) with the generator log
— see [Troubleshooting](/guide/troubleshooting).

## DM0002 {#dm0002}

**A service type cannot be constructed and was not registered.**

The implementation is abstract or a static class, so the container could not construct it.

## DM0003 {#dm0003}

**A module marked with `[DependencyModule]` is not partial.**

The generator completes the module's partial declaration. Without `partial` there is nothing to
complete.

## DM0004 {#dm0004}

**Two conventions in one module register a type as the same service type.**

The lifetime would be ambiguous.

```csharp
conventions.RegisterAll<IRepository>().AsScoped();
conventions.RegisterAll<IRepository>().AsSingleton();   // DM0004
```

Equal lifetimes are an error too — the declaration is redundant.

A type filling two *different* roles is not ambiguous and registers as both. Conventions in different
modules never collide; each registers into its own realm.

## DM0005 {#dm0005}

**A convention matched no types.**

Almost always a renamed interface or a typo in a filter.

## DM0006 {#dm0006}

**A convention matched a type with no accessible constructor.**

The container could not construct it.

## DM0007 {#dm0007}

**Two decorators of one service share an order.**

Their nesting would be ambiguous. See [Decorators](/guide/decorators#ordering).

## DM0008 {#dm0008}

**A service marked for interception cannot be wrapped.**

The member uses `ref`, `in`, `out` or a `ref struct` parameter, returns by reference, has an
`init`-only setter, is static, or the implementation is generic. See
[Interception](/guide/interception#what-cannot-be-intercepted).

## DM0009 {#dm0009}

**A convention declaration could not be read.**

The `Conventions` body is read at compile time, so only the documented calls can appear in it — a
loop, a conditional, a local or a call to your own helper cannot.

It also covers a convention with no lifetime, a `RegisterAll()` with no shape or no filter, and a
chain that could not be resolved.

## DM0010 {#dm0010}

**A service is registered by convention.**

Informational, reported at the class, naming the service type it was registered as and the interface
the match came through when it was not direct.

A match from a [referenced assembly](/guide/scanning) has no class to point at, so it reports at the
`RegisterAll` line instead.

## DM0011 {#dm0011}

**A service is registered only when an environment condition holds.**

Informational, reported at the class. See [Environments](/guide/environments).

## DM0012 {#dm0012}

**An environment condition names nothing to test.**

`[IfEnvironment()]` and `[IfEnvironmentValue("")]` both compile. Written plain they mean the service
never registers; written as the `IfNot` form they mean the attribute does nothing at all.
