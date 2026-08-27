# A run-time view of the composed module graph

The generator knows, at compile time, exactly which modules a composition loads and what each one
registers. None of that is reachable at run time. The plugin-host application in the 1.1.0 field
round wanted it and invented an `IPluginDescriptor` convention every plugin author has to remember
to implement — a hand-maintained copy of something the generator already had.

## What was wanted

- **What is loaded?** A host listing its plugins, a diagnostics endpoint, a startup banner.
- **Where did this registration come from?** Answering "why is `IFoo` the wrong implementation" means
  knowing which module registered it, which today means reading generated code.
- **Did the module I expect actually load?** The failure DM0019 exists for is an assembly attribute
  nobody read. A host that could ask would not need the diagnostic to catch it.

## What already exists

`DependencyRegistry<TModule>` holds the applied modules while the container is being built, and
`IDependencyModule` is the shape each one implements. The information is present during
`LoadModules`; nothing survives into the built provider.

The generated code also already knows the shape — `InternalGetModules` names each composed module,
and the dependency methods name each registration.

## The shape of an answer

### A. Register the module list

The smallest useful thing: `LoadModules` registers what it loaded.

```csharp
public interface IModuleGraph {
    IReadOnlyList<Type> Modules { get; }
    bool Contains<TModule>() where TModule : IDependencyModule;
}
```

*For:* a few lines, no generator change, and it answers "what is loaded?" and "did it load?" — the
two questions with the sharpest failure modes today.
*Against:* says nothing about registrations, so "which module registered `IFoo`?" is unanswered.

### B. The full graph, emitted

The generator emits, per module, the service types it registers, and the runtime assembles them:

```csharp
public interface IModuleGraph {
    IReadOnlyList<ModuleNode> Modules { get; }
}

public record ModuleNode(Type ModuleType, IReadOnlyList<Type> Services, IReadOnlyList<Type> Composes);
```

*For:* answers everything above, and makes a "why is this registration here" diagnostic page
possible without reading generated code.
*Against:* real new surface — every registration shape has to be represented, including conditional
ones whose presence is a run-time question, and generic ones whose closure is per-registration. Cost
in generated code size and startup for every project, to serve a case most do not have.

### C. Opt in

B, behind `DependencyModules_EmitModuleGraph`, so a project that wants it pays for it.

*For:* keeps the cost off projects that do not need it, and matches how `GenerateFactories` is
handled.
*Against:* a fourth MSBuild property, and a library whose behaviour differs by build configuration —
which is exactly the shape that made `GenerateFactories` × interception hard to find.

## What has to be decided before this is written

1. **Which of the three questions is in scope?** A answers two of them for almost nothing. B answers
   all three at real cost. That is the decision; the rest follows.
2. **Conditional registrations.** `[IfEnvironment]` makes a registration's presence a run-time fact.
   The graph either reports what was *declared* or what was *applied*, and those differ. The second
   is more useful and needs the runtime, not the generator, to build it.
3. **Trimming and AOT.** A graph of `Type` objects is the kind of thing that keeps metadata alive.
   Whatever ships has to be measured against the 2.6 MB the round recorded, not assumed to be free.
4. **Is this public API or a diagnostic aid?** If it is for humans reading a page, it can be a string
   rendering and stay outside the semver promise. If it is for code to route on, it is API for 1.x.

## Not doing it in 1.2.0

Option A is genuinely small and could ship on its own. It is held back with B and C because shipping
`IModuleGraph` with two of three questions answered fixes the name — and the fuller version then
either breaks it or arrives as a second, differently-named thing.

Worth noting what the workaround cost: an interface every plugin author has to remember, with no
diagnostic when they forget. That is the failure mode this library is otherwise built to remove.
