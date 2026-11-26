# Design: DependencyModules in VerificationGlue.Tests, with no adapter

Status: **design, for review before any code is written.** It replaces the adapter package of the
closed #134 and the "DependencyModules adapter" section of VerificationGlue's
`docs/design/tests.md`.

A `[VerifyTest]` test gets DependencyModules support from attributes the user already writes. A
module attribute such as `[SutModule]` on the assembly, the class or the method loads that module
into the test's container. The test then takes the module's services as parameters. A `[Mock]`
parameter replaces the matching service. No adapter attribute and no adapter package exist.

```csharp
using Shop;
using VerificationGlue.Mocks;
using VerificationGlue.Tests;
using static VerificationGlue.VerifySyntax;

[SutModule]
public partial class OrderServiceTests
{
    [VerifyTest]
    public void Create_stamps_the_order_with_the_clock([Mock] IClock clock, OrderService sut, IOrderStore store)
    {
        clock.Now.Returns(new DateTime(2026, 11, 17));

        var order = sut.Create(quantity: 2, price: 10);

        Verify(order.Created == new DateTime(2026, 11, 17));
        Verify(store.Orders.Count == 1);
    }
}
```

The work splits across the two repositories:

- VerificationGlue's generator recognizes module attributes and writes the container code. Part 1
  covers it.
- `[Container<T>]` accepts a module as well as a bootstrap. Part 2 covers it, with the answer to the
  `new()` proposal.
- DependencyModules.Testing gets one entry point that every runner calls. Part 3 gives its exact
  signature.
- Parts 4 to 8 cover `[Mock]`, shared scopes, `CurrentTest`, precedence and the diagnostics of the
  two generators.
- Part 9 shows the user's code and the generated code for each case.
- The last sections list the new diagnostics, the changes in landing order and the open questions.

---

## Background: what exists today

### DependencyModules

- A module is a `[DependencyModule] partial class`. The generator completes it with
  `IDependencyModule` and writes a `{Name}Attribute` that implements `IDependencyModuleProvider`.
  The attribute's constructor and settable properties mirror the module's. Its `GetModule()`
  constructs the module from them. `ModuleWithConstructorParametersAndProperties.verified.txt`
  shows `new TestModuleAttribute(bool someFlag)` calling `new TestModule(someFlagField)`.
- `AddModules(params IDependencyModule[])` calls `DependencyRegistry<object>.LoadModules`, which
  de-duplicates modules by `Equals`. The generated `Equals` compares by type. DM0018 warns when a
  module with properties relies on it.
- `ModuleTestCase.SetupServiceCollection` (xUnit) and `ModuleTestCommand.Execute` (NUnit) build a
  test's container in the same order:
  1. Register `ITestCaseInfo` and an `ITestContainerSource`.
  2. Register the environment from the narrowest `IModuleEnvironmentProvider` that answers.
  3. Load the modules from every `IDependencyModuleProvider` attribute and from `[ModuleTest]`, with
     the list reversed.
  4. Run `ITestServiceSetupAttribute`s, mock support first. `[TestExport]` is one.
  5. Run each parameter's `ITestParameterValueProvider.SetupServiceCollection`. `[Mock]` is one.
  6. Build with the narrowest `IServiceProviderBuilderAttribute`, or `BuildServiceProvider()`.
  7. Run `ITestStartupAttribute`s.
  8. Initialize the `TestContainerSource` with the pinned services from `SharedRegistrations`.
  9. Resolve the arguments with `TestParameterResolver`.

  The two copies differ only in xUnit's `async` against NUnit's `GetAwaiter().GetResult()`.
- DependencyModules.Testing references `Microsoft.Extensions.DependencyInjection.Abstractions` and
  `Microsoft.Extensions.Logging.Abstractions`. It does not reference DependencyModules.Runtime.
  `IModuleEnvironmentProvider` lives in Runtime for that reason, as its remarks say.
- `CurrentTest.Provider` is a settable static. Each runner package installs its
  `ICurrentTestProvider` only when none is set.
- DM0021 is the only generator diagnostic about test attributes. It reports a `[Mock]` parameter
  and a `[TestExport]` on the same method that name one service.

### VerificationGlue.Tests

- `TestGenerator` registers one `TestDescriptor` per test from a module initializer.
  `AttributeMaterializer` writes each attribute of the assembly, class, method and parameter as
  `new XAttribute(args)`. An attribute whose type does not bind is left out.
- `TestAnalyzer` reports VGT0006 for an attribute the generator leaves out. The analyzer runs on the
  compilation that includes every generator's output, where a DM-generated attribute binds. The
  generator does not see that output. VG's `docs/design/tests.md` says the test-project module case
  reports VGT0006. I did not build that case, so which of the two views wins there is unverified.
- `ContainerAttribute<T> where T : ITestContainerBootstrap` holds `internal BuildAsync`, which calls
  the static `T.BuildAsync`. The runner takes the last `ContainerAttribute` in the test's
  attribute list, which is the narrowest. A shared container is keyed by `Bootstrap`, which is
  `typeof(T)`.
- `TestRunner.AddMocks` creates one mock per `(Type, ServiceKey)` of the `[Mock]` parameters and
  adds each as a `TestOverride` before the container is built. `TestDiscovery.IsMock` recognizes
  only `VerificationGlue.Mocks.MockAttribute`.
- The mock generator also treats every name in the `VerificationGlueMockAttribute` MSBuild item as a
  `[Mock]` (`MockDiscovery.ReadMockAttributes`). The test generator does not read that item.
- `ITestContainer.TryResolve` is synchronous. `TestContainerContext` carries the scope, the
  attributes, the parameters and the overrides. It carries no `MethodInfo` and no `TestContext`.
- A VerificationGlue.Tests project is an executable. Microsoft.Testing.Platform's MSBuild targets
  write its entry point.

### Hardened.Framework

A Hardened test project names its application with `[assembly: HardenedTestEntryPoint(typeof(TodosLibrary))]`.
That attribute comes from the referenced `Hardened.Shared.Testing` and implements
`IDependencyModuleProvider`, `IModuleEnvironmentProvider`, `ISharedTestRegistration`,
`ITestServiceSetupAttribute` and `ITestStartupAttribute`. `[WebTesting]` implements the last three
and names its client parameters in `ISharedTestRegistration.IsolatedServices`. `[Grants]`,
`[Subject]`, `[Claim]` and `[Anonymous]` are parameter-level `ITestParameterValueProvider`s. The web
hosts build a container per request through `ITestContainerSource`. The attributes read the test's
attributes by reflection from `ITestMethodContext.Method` (`AttributeCollection.FromMethodInfo`).
`HardenedTestEntryPointAttribute.SetupServiceCollection` writes the log to the test's output only
when `CurrentTest.Provider` is set.

So Hardened gets VerificationGlue support from this design when four things hold:

- The recognizer accepts any attribute that implements `IDependencyModuleProvider`, not only
  DM-generated ones.
- The entry point runs the whole build order above, `ITestContainerSource` included.
- VerificationGlue hands the entry point a real `MethodInfo`.
- `CurrentTest.Provider` is set in a VerificationGlue run.

Hardened's own `[HardenedModule]` is not recognized by name in a test project. Open question 5
covers it.

### What #134 had to map, and what it hit

The adapter package mapped modules, `[TestExport]`, environments, startup attributes, provider
builders, `[InjectValues]`, keyed services and `[Shared]` onto shared scopes. It hit five problems.
This design answers each:

| Problem in #134 | Answer here |
|---|---|
| A module declared in the test project needed `ModuleTestAttribute<TModule>`, because VG's generator cannot see the DM-generated attribute. | The generator resolves the attribute by name (Part 1). |
| VG gives a container no `MethodInfo`, so the package searched for it by reflection from the test UID. | The generator writes a typed lookup for the method (Part 3, "The method"). |
| DM's `[Mock]` failed with a message naming an alias fix. | DM's `[Mock]` is a VG mock (Part 4). |
| `CurrentTest` was not mapped, because VG has no ambient test. | VG adds `TestContext.Current`, and generated code installs a provider (Part 6). |
| `IModuleEnvironmentProvider` and `ITestStartupAttribute` did not run in a class or assembly container. | Still true for the shared parent. Open question 3 covers it. |

---

## Part 1: recognizing module attributes

### The rule

A module attribute usage is an attribute on the assembly, the test class or the test method that
meets one of two conditions:

1. **It binds to a type that implements `DependencyModules.Runtime.Interfaces.IDependencyModuleProvider`.**
   This is a module from a referenced assembly. It also covers `[HardenedTestEntryPoint]` and any
   other referenced attribute that provides a module.
2. **It binds to an error type, and the name resolves to a module in this compilation.** DM's
   generator writes `{Name}Attribute` into the same compilation, and VG's generator cannot see it.
   The generator looks the name up at the attribute's position:
   - It takes the attribute name as written, such as `SutModule`, `SutModuleAttribute` or
     `Shop.Tests.SutModule`. It drops the `Attribute` suffix.
   - It binds a qualifier through the semantic model. The namespace binds even though the
     attribute does not.
   - It calls `SemanticModel.LookupNamespacesAndTypes(position, container, name)`. A module class is
     in scope wherever its attribute is, because DM writes the attribute into the module's
     namespace.
   - It accepts the result only when it is a class marked
     `[DependencyModules.Runtime.Attributes.DependencyModule]` whose `GenerateAttribute` is not
     `false`. The attribute's full name is then the module's namespace plus `{Name}Attribute`.

Recognition compares fully qualified names. It does not compare shapes. The names are
`DependencyModules.Runtime.Interfaces.IDependencyModuleProvider`,
`DependencyModules.Runtime.Interfaces.IDependencyModule` and
`DependencyModules.Runtime.Attributes.DependencyModuleAttribute`.

The generator writes DependencyModules code only when
`DependencyModules.Runtime.Interfaces.IDependencyModule` resolves in the compilation. Without it,
nothing in this design runs, and VG behaves exactly as today.

### Two modes, chosen by what resolves

| Resolves in the compilation | Mode | The container |
|---|---|---|
| `IDependencyModule`, and `Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions` | Runtime | `AddModules`, the overrides as instances, `BuildServiceProvider()`. `Scope = ContainerScope.Test` only. |
| The above, and `DependencyModules.Testing.Impl.ModuleTestComposition` | Testing | The entry point of Part 3 builds it. Every scope is supported. |

The Testing mode is chosen by the entry point type, not by the assembly. An older
DependencyModules.Testing without the entry point gets the Runtime mode and VGT0011 (see "New
diagnostics"). Without that warning its `[TestExport]` would do nothing and nothing would say so.

### Materializing a test-project module attribute

`AttributeMaterializer.TryCreate` leaves out an error-type attribute today. For a usage that the
rule resolves by name, it writes `new global::Shop.Tests.SutModuleAttribute(args) { Name = value }`
instead. The final compilation binds that expression, because DM's attribute exists there.

`AttributeData` of an error type carries no constructor arguments. So the generator reads them from
the `AttributeArgumentSyntax` list:

- A positional or named argument whose `SemanticModel.GetConstantValue` has a value is written as
  that constant.
- A `typeof` argument is written as `typeof`.
- Any other argument leaves the attribute out, with VGT0006 and the reason "an argument cannot be
  written as C#".
- The positional count must match a constructor of the module class. A mismatch leaves the
  attribute out. The C# compiler reports the user's own line in that case, and the generated file
  must not report it a second time.

Writing the attribute rather than the module means DM keeps the mapping from attribute arguments to
module constructor and properties. VG never copies that mapping.

### The analyzer and the generator must agree

`TestAnalyzer` sees the compilation with DM's output, where every module attribute binds and every
module class implements `IDependencyModule`. The generator sees neither. Each rule in this design
therefore lives in `TestDiscovery`, which both call. Each rule has two paths, one for the bound type
and one for the name lookup. A spec compiles the same test with and without DM's generator and
checks that both paths give the same answer.

---

## Part 2: `[Container<T>]` for modules, and the `new()` proposal

### What changes either way

`ContainerAttribute<T>.BuildAsync` calls `T.BuildAsync`, which needs the
`ITestContainerBootstrap` constraint. VG.Tests cannot name DependencyModules, so the attribute
cannot build a module container itself. In both options the generator writes the build call, and
the runner takes it from the descriptor:

```csharp
// VerificationGlue.Tests, new
public sealed class TestContainerBinding
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public TestContainerBinding(Func<TestContainerContext, ValueTask<ITestContainer>> build, bool supportsChildOverrides);

    public Func<TestContainerContext, ValueTask<ITestContainer>> Build { get; }
    public bool SupportsChildOverrides { get; }
}
```

`TestDescriptor` takes it as the optional `container:` argument. The runner keeps reading `Scope`
and the shared-container key from the narrowest `ContainerAttribute`. It reads `Build` and
`SupportsChildOverrides` from the binding. A test with a binding and no `ContainerAttribute` is a
test-scoped container, which is the recognized-module case of Part 1.
`ContainerAttribute.BuildAsync` is removed. It is internal, so its removal is not an API change.

### The two options

**A. `where T : new()`**, the owner's proposal, with VGT0009 for a `T` the generator cannot wire.

**B. No constraint**, with the generator writing `T.BuildAsync(context)` for a bootstrap and the
module wiring of Part 9 for a module. VGT0009 is the same.

| | A: `new()` | B: no constraint |
|---|---|---|
| A bootstrap | Must have a public parameterless constructor. Every bootstrap in VG's samples, and `WebAppBootstrap<TEntry>`, has the implicit one. A bootstrap with a private constructor fails with CS0310, although nothing ever constructs a bootstrap. | Any type that implements `ITestContainerBootstrap`. The generator calls the static method and never constructs it. |
| A module with constructor parameters, such as `RegionModule(string region)` | `[Container<RegionModule>]` fails with CS0310 at the use site. So such a module cannot have a class or assembly scope at all, because module attributes have no `Scope`. | `[Container<RegionModule>(Scope = ContainerScope.Class)]` works when a `[RegionModule("eu")]` on the owning scope supplies the arguments. Without one, VGT0009 names the fix. |
| What the constraint guarantees | Almost nothing. `[Container<int>]` and `[Container<object>]` satisfy `new()`. VGT0009 still does all the checking. | Nothing. VGT0009 does all the checking. |
| Native AOT | Safe, because generated code does the construction. `new T()` in the attribute would compile to `Activator.CreateInstance<T>()`, which AOT supports, but nothing calls it. | Safe, for the same reason. Every call is a direct, typed call in generated code. |
| Public API in a 0.x preview | A different constraint. Usages of bootstraps without a parameterless constructor stop compiling. A derived `X<T> : ContainerAttribute<T> where T : ITestContainerBootstrap` stops compiling until it adds `new()`. An assembly compiled against 0.3 with such a type fails to load. | A removed constraint. Every usage and every derived attribute that compiles today still compiles. A compiled derived type still loads. |

**Recommendation: B, no constraint.** `new()` costs a source and binary break, and it blocks the
one module case that needs `[Container<T>]` most. It also checks nothing that VGT0009 does not
already check. The only thing it would allow is the runtime constructing `T` without generated
code, and the runtime has nothing to do with a `T` it cannot name as a bootstrap or a module.

`Bootstrap` keeps its name and returns `typeof(T)` for a module too. Renaming it would be a break
for no behaviour.

### What the generator accepts as `T`

| `T` | Wired as |
|---|---|
| Implements `VerificationGlue.Tests.ITestContainerBootstrap` | `static context => T.BuildAsync(context)`. `SupportsChildOverrides` is whether `T` implements `ISupportsChildOverrides`. |
| Implements `IDependencyModule`, or is a class marked `[DependencyModule]` (Part 1), with an accessible parameterless constructor | The module container, with `static () => new T()` as the container module. `SupportsChildOverrides` is `true` in the Testing mode. |
| A module whose constructor needs arguments | The module container with no container module. A module attribute for `T` in scope supplies the instance. For a class or assembly scope, the attribute must be on that scope. |
| Both a bootstrap and a module | VGT0010. |
| Anything else, including an error type that does not resolve to a module | VGT0009. |

A derived attribute such as `WebAppAttribute<TEntry> : ContainerAttribute<WebAppBootstrap<TEntry>>`
is read through its base type, as `TestDiscovery.TypeArgument` does today.

---

## Part 3: the DependencyModules.Testing entry point

### The signature

This is the contract between the repositories. It uses no VerificationGlue type.

```csharp
namespace DependencyModules.Testing.Impl;

/// Builds a test's container from modules and testing attributes, the same way for every runner.
public static class ModuleTestComposition
{
    public static ValueTask<ModuleTestContainer> BuildAsync(
        IServiceCollection services,
        IReadOnlyList<IDependencyModule> modules,
        ITestMethodContext testMethod,
        IReadOnlyList<TestServiceOverride> overrides,
        ICurrentTestProvider? currentTest,
        ModuleTestContainer? parent);

    public static ValueTask<ModuleTestContainer> BuildScopeAsync(
        IServiceCollection services,
        IReadOnlyList<IDependencyModule> modules,
        IReadOnlyList<Attribute> scopeAttributes);
}

/// An instance the container returns for a service type and an optional key, such as a mock.
public sealed record TestServiceOverride(Type ServiceType, object Instance, object? ServiceKey);

/// The plain ITestMethodContext, for a runner that has no test-method model of its own.
public sealed class TestMethodContext(MethodInfo method, IReadOnlyList<Attribute> attributes) : ITestMethodContext
{
    public MethodInfo Method { get; }
    public IReadOnlyList<Attribute> Attributes { get; }
}

/// A built test container, and every container its ITestContainerSource built.
public sealed class ModuleTestContainer : IAsyncDisposable
{
    public IServiceProvider Services { get; }

    /// The arguments for the whole method, as the xUnit and NUnit packages resolve them today.
    public Task<object?[]> ResolveArgumentsAsync(object?[] data);

    /// The value of the ITestParameterValueProvider attributes on the parameter, or null when none answers.
    public Task<object?> GetParameterValueAsync(ParameterInfo parameter);

    /// IServiceProvider, a keyed service, a registered service, or an unregistered non-abstract
    /// class built with its [InjectValues]. Null when none applies.
    public object? Resolve(ParameterInfo parameter);

    /// A keyed or unkeyed registered service, or null.
    public object? Resolve(Type serviceType, object? serviceKey);

    public ValueTask DisposeAsync();
}
```

What each argument of `BuildAsync` holds:

- **`services`** is the collection to build into. A runner registers its own services first, such as
  xUnit's `ITestCaseInfo`.
- **`modules`** are listed in declaration order. The runner's own modules come first:
  `[ModuleTest(typeof(X))]` for xUnit and NUnit, `[Container<X>]` for VG. The module attributes
  follow, widest scope first. The entry point reverses the list before `LoadModules`, as
  `ModuleTestCase.SetupModules` does, so the load order does not change for xUnit or NUnit.
- **`testMethod`** carries the `MethodInfo` and the attribute instances, widest scope first.
  `ITestMethodContext.Attributes` already exists for these instances. A second parameter for them
  could disagree with it, so the owner's "testing attribute instances" travel there.
- **`overrides`** are registered last, as instances, under their key when they have one. A
  parameter whose type and key an override names is supplied by that override. Its
  `ITestParameterValueProvider` attributes are not run in either phase. This is how DM's `[Mock]`
  stands aside for VG's mock (Part 4).
- **`currentTest`** is registered as the container's `ICurrentTestProvider` when it is not null.
  `CurrentTest.Provider` is not touched.
- **`parent`** is a class or assembly container from `BuildScopeAsync`, or null (Part 5).

### The order of `BuildAsync`

The steps are the ones xUnit and NUnit take today. The new steps are marked.

1. Register an `ITestContainerSource`.
2. **New:** register `currentTest`.
3. Register the environment from the narrowest `IModuleEnvironmentProvider` in
   `testMethod.Attributes` that answers.
4. Load `modules`, reversed, with `DependencyRegistry<object>.LoadModules`.
5. Run the `ITestServiceSetupAttribute`s in `testMethod.Attributes`, the `IMockSupportAttribute`s
   first.
6. **New:** when `parent` is set, register the parent's instances of the shared services (Part 5).
7. Run `ITestParameterValueProvider.SetupServiceCollection` for each parameter of
   `testMethod.Method` that no override names.
8. **New:** register `overrides` as instances.
9. Build with the narrowest `IServiceProviderBuilderAttribute`, or `BuildServiceProvider()`.
10. Run the `ITestStartupAttribute`s.
11. Initialize the `TestContainerSource`. It uses `SharedRegistrations`, the same builder and the
    same startup. `ModuleTestContainer` tracks every container the source builds.

When a step throws, the entry point disposes every container it built and rethrows. NUnit's
`finally` does that today, and VG needs it because it never receives a container to dispose.

Overrides go after the setup attributes. So a `[Mock]` beats a `[TestExport]` naming the same
service under VG too, which is the rule DM0021 is written against (Part 8).

`BuildScopeAsync` loads `modules` with the default environment. It registers the
`[TestExport(Shared = true)]` attributes in `scopeAttributes` and builds with
`BuildServiceProvider()`. It runs nothing that needs a test method, because there is none.

### What moves

- `ModuleTestCase.SetupServiceCollection`, `StartAsync`, `BuildServiceProvider`,
  `SetupServiceSetupAttributes`, `SeedEnvironment`, `SetupModules` and `DisposeProviders` become
  one call to `BuildAsync` and one `DisposeAsync`. xUnit keeps discovery, data rows, `TestCaseInfo`
  and test creation. xUnit4 compiles the same sources.
- NUnit's `ModuleTestCommand` does the same, with `GetAwaiter().GetResult()` as now.
- `TestParameterResolver`, `SharedRegistrations` and `TestContainerSource` stay public and
  unchanged. The entry point uses them.

### New package references

DependencyModules.Testing gains references to DependencyModules.Runtime, for `IDependencyModule`,
`IModuleEnvironmentProvider` and `DependencyRegistry`, and to
`Microsoft.Extensions.DependencyInjection`, for `BuildServiceProvider()`. The xUnit and NUnit
packages reference both already. The mock packages get them through DependencyModules.Testing.
Open question 1 covers the cost. DependencyModules.Testing references no VerificationGlue package,
and no DM package does.

### The method

`ITestMethodContext.Method` is a `MethodInfo`, and Hardened's attributes read it by reflection. So
VG supplies one. The generator writes, per test, a typed lookup that ILLink and the AOT compiler can
analyze:

```csharp
method: static () => typeof(global::Shop.Tests.OrderServiceTests).GetMethod(
    "Create_stamps_the_order_with_the_clock",
    global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.DeclaredOnly,
    new global::System.Type[] { typeof(global::Shop.IClock), typeof(global::Shop.OrderService), typeof(global::Shop.IOrderStore) })!
```

The generator writes it only in the Testing mode. A VG project without DependencyModules keeps
running with no reflection.

---

## Part 4: `[Mock]`

`TestDiscovery.IsMock` and `MockDiscovery` both accept `DependencyModules.Testing.Attributes.MockAttribute`
as well as `VerificationGlue.Mocks.MockAttribute`. The descriptor writes
`static () => global::VerificationGlue.Mocks.Mock.Of<T>()` for a parameter that carries either.
The mock generator writes the mock for either. The runner adds it as an override, so the system
under test receives it. One mock is shared per type and key, as today.

The name is added to both generators as a constant in `src/Shared.Generator`, rather than through
the `VerificationGlueMockAttribute` MSBuild item. The test generator does not read that item, and a
constant also works in the generator specs, which import no MSBuild props.

DM's NSubstitute, Moq and FakeItEasy support stays an xUnit and NUnit feature. Under VG an
`IMockSupportAttribute` on the assembly is still run as an `ITestServiceSetupAttribute` in step 5.
It never creates a `[Mock]` parameter's double, because step 7 skips a parameter that an override
names.

**The name clash.** Either `MockAttribute` now does the same thing, so the two no longer disagree in
meaning. The compiler still reports CS0104 on `[Mock]` in a file that imports both
`VerificationGlue.Mocks`, for `Returns`, and `DependencyModules.Testing.Attributes`, for
`[TestExport]` or `[Shared]`. That is the common case. Open question 4 covers the fix.

---

## Part 5: shared scopes and `[Shared]`

`[Container<TModule>(Scope = ContainerScope.Class)]` or `Scope = ContainerScope.Assembly` gives a
shared parent and a child per test. A recognized module attribute without `[Container<T>]` is always
test-scoped, because module attributes have no `Scope`.

- **The parent** is `BuildScopeAsync` over the owning scope's modules and attributes. It never gets
  overrides, so no test's mock reaches an instance another test sees.
- **A child** is a full `BuildAsync` for the test, with the test's own attributes, modules,
  environment and overrides, and with `parent` set. Step 6 registers, as instances from the parent:
  - the type of each method parameter marked `[Shared]`,
  - the `Service` of each `[TestExport(Shared = true)]` on the owning scope.

  A type that an `ISharedTestRegistration.IsolatedServices` names is never taken from the parent.
  Such a type builds containers, and a parent's one would send every test's requests to the
  parent. The parent resolves each shared type once and keeps it. A transient registration is
  therefore one instance per scope too. That is what `[Shared]` asks for.
- Overrides come after step 6, so a test's `[Mock]` replaces a shared service of the same type.
- The module container implements child overrides, so VGT0008 never applies to it.
- Under `Scope = ContainerScope.Test`, `[Shared]` keeps its DependencyModules meaning. It pins a
  parameter across the containers that the test's `ITestContainerSource` builds.

The Runtime mode supports `Scope = ContainerScope.Test` only. Without DependencyModules.Testing no
`[Shared]` exists, so a child could share nothing. VGT0009 reports a class or assembly scope there.

---

## Part 6: `CurrentTest`

VG adds an ambient test and three members. The generated code uses them for an
`ICurrentTestProvider` that has no framework dependency:

```csharp
// VerificationGlue.Tests, new members
public sealed class TestContext
{
    public static TestContext? Current { get; }     // AsyncLocal, set while the test runs
    public Type TestClass { get; }
    public MethodInfo? Method { get; }              // from the descriptor's method:, or null
    public bool IsRunning { get; }                  // false once the test has ended
}

public sealed class TestContainerContext
{
    public TestContext? Test { get; }               // null for a class or assembly container
}
```

In the Testing mode the generator writes one provider class into the test assembly:

- A module initializer installs one instance with `CurrentTest.Provider ??=`. It reads
  `TestContext.Current`, so DM's static `CurrentTest` works in a VG run.
- Each test container gets an instance bound to that test's `TestContext`, through `currentTest`.
  That instance answers for its test on any thread, including threads that did not flow the
  `AsyncLocal`.

The members map one to one: `Key` is the `TestContext`, `DisplayName` is its `DisplayName`, and
`Assembly` is `TestClass.Assembly`. `TryWriteLine` writes to `Output` and returns `false` once
`IsRunning` is false.

---

## Part 7: precedence

For each test the generator decides the container in this order. The analyzer decides it in the
same order, because both call `TestDiscovery`.

1. **An explicit `[Container<T>]` wins.** The narrowest one decides: the method's, then the
   class's, then the assembly's. Part 2 gives how `T` is wired.
   - When `T` is a bootstrap, recognized module attributes still reach it in `context.Attributes`,
     and VG does nothing else with them.
   - When `T` is a module, the module attributes in scope load with it, as `[ModuleTest(typeof(X))]`
     and `[SutModule]` load together under xUnit. DM de-duplicates a module that both name.
2. **Otherwise each recognizer is asked.** The DependencyModules recognizer of Part 1 claims a test
   when a module attribute is in scope on the assembly, the class or the method. The container is
   test-scoped.
3. **Two recognizers claiming one test is VGT0010.** Only one recognizer exists today. The
   generator keeps an internal recognizer list, and the rule and the diagnostic exist before a
   second recognizer does. VGT0010 also covers a `T` that is both a bootstrap and a module, which is
   two ways to wire one container.

---

## Part 8: the diagnostics of the two generators

### DM0021 against VG tests

DM0021 (`TestAttributeDiagnostics`) reads every method that has attributes and parameters. It is not
tied to a runner, so it reads `[VerifyTest]` methods whenever DM's generator is in the test project.
DM's generator is always there when the test project declares a module.

| DM0021's assumption | Under VG with this design |
|---|---|
| The parameter wins over a `[TestExport]` on the same method. | True. Overrides are step 8, after the setup attributes in step 5. |
| A keyed `[Mock]` does not replace the unkeyed export. | True. The override is registered under its key. |
| A `[Mock] T` paired with `Moq.Mock<T>` registers nothing. | Moq has no meaning under VG. The exemption stays and is harmless. |
| `[Mock]` means `DependencyModules.Testing.Attributes.MockAttribute`. | **Not enough.** VG's own `VerificationGlue.Mocks.MockAttribute` has the same precedence and is not recognized. |

**Change in DM:** `TestAttributeDiagnostics` also accepts `VerificationGlue.Mocks.MockAttribute`, by
fully qualified name. It must not reference VerificationGlue. **Change in VG:** none. VG must not
add its own `[Mock]` against `[TestExport]` diagnostic, because DM0021 already reports it and a
second report would be a duplicate.

### The others

- **VGT0006** must not report a module attribute that Part 1 resolves by name, because the
  generator now writes it. `TestAnalyzer.Check` skips a usage that `TestDiscovery` recognizes.
- **VGT0008** never reports a module container in the Testing mode, because its binding says it
  supports child overrides. In the Runtime mode, VGT0009 reports the shared scope first.
- **VGM0001**, "type cannot be mocked", is reported by VG's mock generator for a DM `[Mock]`
  parameter too. DM reports nothing about whether a type can be mocked, so the two reports do not
  overlap.
- **DM0016 and DM0019** read assembly-level module attributes. In a VG project, DM0016 is right
  unchanged. DM0019 reports only when an `ApplicationModule` was generated, and DM generates one
  only from a file named `Program.cs` (`BaseSourceGenerator.CreateSourceValueProvider`,
  `ApproveFilter`). MTP writes the entry point of a VG project. I did not check that file's name.
  If it were `Program.cs`, `[assembly: SutModule]` in any other file would report DM0019 wrongly.
- DM's `MockAttribute` remarks say that it throws without a mock-support attribute. The remarks get
  a sentence saying that under VerificationGlue it is a VerificationGlue mock.

---

## Part 9: the cases

Every generated example below is what the generator would write. None of it has been compiled. The
descriptor excerpts show only the arguments this design adds or changes. The arguments that today's
`GeneratedCodeSpecs` snapshots already show are elided with `...`. The assembly file is written
without `global::` here, for reading. The generator writes every name with `global::`, as it does
today.

### The assembly file, written once

The generator writes `VerificationGlue.Tests.DependencyModules.g.cs` once per assembly in which some
test uses a module container. This is the Testing mode:

```csharp
// <auto-generated/>
#nullable enable
#pragma warning disable
namespace VerificationGlue.Tests.Generated
{
    file static class DependencyModulesRegistration
    {
        [ModuleInitializer]
        internal static void Register() =>
            CurrentTest.Provider ??= new DependencyModulesCurrentTest(null);
    }

    internal sealed class DependencyModulesContainer : ITestContainer
    {
        private readonly ModuleTestContainer _container;
        private readonly Func<IDependencyModule>? _containerModule;
        private readonly MethodInfo? _method;
        private readonly Dictionary<TestParameter, object> _provided = new();

        private DependencyModulesContainer(ModuleTestContainer container, Func<IDependencyModule>? containerModule, MethodInfo? method)
        {
            _container = container;
            _containerModule = containerModule;
            _method = method;
        }

        public IServiceProvider Services => _container.Services;

        public static async ValueTask<ITestContainer> BuildAsync(TestContainerContext context, Func<IDependencyModule>? containerModule)
        {
            if (context.Test is null)
            {
                var scope = await ModuleTestComposition.BuildScopeAsync(
                    new ServiceCollection(), Modules(context, containerModule), context.Attributes);
                return new DependencyModulesContainer(scope, containerModule, null);
            }

            return await BuildTestAsync(context, containerModule, parent: null);
        }

        public ValueTask<ITestContainer> CreateChildAsync(TestContainerContext context) =>
            BuildTestAsync(context, _containerModule, _container);

        private static async ValueTask<ITestContainer> BuildTestAsync(
            TestContainerContext context, Func<IDependencyModule>? containerModule, ModuleTestContainer? parent)
        {
            var test = context.Test!;
            var overrides = new List<TestServiceOverride>(context.Overrides.Count);
            foreach (var entry in context.Overrides)
            {
                overrides.Add(new TestServiceOverride(entry.ServiceType, entry.Instance, entry.Key));
            }

            var built = await ModuleTestComposition.BuildAsync(
                new ServiceCollection(),
                Modules(context, containerModule),
                new TestMethodContext(test.Method!, context.Attributes),
                overrides,
                new DependencyModulesCurrentTest(test),
                parent);

            var container = new DependencyModulesContainer(built, containerModule, test.Method);
            try
            {
                await container.ProvideAsync(context.Parameters);
            }
            catch
            {
                await built.DisposeAsync();
                throw;
            }

            return container;
        }

        // DependencyModules parameter attributes, such as Hardened's [Grants], are asynchronous and
        // ITestContainer.TryResolve is not. They run here, after startup, which is where xUnit runs them.
        private async ValueTask ProvideAsync(IReadOnlyList<TestParameter> parameters)
        {
            var methodParameters = _method!.GetParameters();
            foreach (var parameter in parameters)
            {
                if (parameter.IsConstructorParameter || parameter.IsMock ||
                    !parameter.Attributes.Any(attribute => attribute is ITestParameterValueProvider))
                {
                    continue;
                }

                if (await _container.GetParameterValueAsync(methodParameters[parameter.Position]) is { } value)
                {
                    _provided[parameter] = value;
                }
            }
        }

        public bool TryResolve(TestParameter parameter, out object? value)
        {
            if (_provided.TryGetValue(parameter, out var provided))
            {
                value = provided;
                return true;
            }

            // VerificationGlue supplies these after the container. Constructing them here would be wrong.
            if (parameter.Type == typeof(CancellationToken) || parameter.Type == typeof(TestContext) || parameter.Type == typeof(ITestOutput))
            {
                value = null;
                return false;
            }

            value = parameter.IsConstructorParameter
                ? _container.Resolve(parameter.Type, parameter.ServiceKey)
                : _container.Resolve(_method!.GetParameters()[parameter.Position]);
            return value is not null;
        }

        public ValueTask DisposeAsync() => _container.DisposeAsync();

        private static IReadOnlyList<IDependencyModule> Modules(TestContainerContext context, Func<IDependencyModule>? containerModule)
        {
            var modules = new List<IDependencyModule>();
            if (containerModule is not null)
            {
                modules.Add(containerModule());
            }

            foreach (var attribute in context.Attributes)
            {
                if (attribute is IDependencyModuleProvider provider)
                {
                    modules.Add(provider.GetModule());
                }
            }

            return modules;
        }
    }

    internal sealed class DependencyModulesCurrentTest(TestContext? test) : ICurrentTestProvider
    {
        private TestContext? Running => (test ?? TestContext.Current) is { IsRunning: true } running ? running : null;

        public object? Key => Running;
        public string? DisplayName => Running?.DisplayName;
        public Assembly? Assembly => Running?.TestClass.Assembly;

        public bool TryWriteLine(string message)
        {
            if (Running is not { } running)
            {
                return false;
            }

            running.Output.WriteLine(message);
            return true;
        }
    }
}
```

The modules come from the attribute instances through `IDependencyModuleProvider.GetModule()`,
which is DM's generated code. `LoadModules` then de-duplicates them. The container module of
`[Container<T>]` is a typed `new T()`.

In the Runtime mode the file holds a smaller `DependencyModulesContainer` and no provider class:

```csharp
public static ValueTask<ITestContainer> BuildAsync(TestContainerContext context, Func<IDependencyModule>? containerModule)
{
    var services = new ServiceCollection();
    services.AddModules(Modules(context, containerModule).ToArray());
    foreach (var entry in context.Overrides)
    {
        if (entry.Key is null) services.AddSingleton(entry.ServiceType, entry.Instance);
        else services.AddKeyedSingleton(entry.ServiceType, entry.Key, entry.Instance);
    }

    return new(new DependencyModulesContainer(services.BuildServiceProvider()));
}
```

Its `TryResolve` reads the keyed or unkeyed service and constructs nothing.

### Case 1: a module from a referenced project

The user writes the test at the top of this page. `SutModule` is declared in the referenced `Shop`
project.

Today the class attribute already materializes, because the type binds. The descriptor gains the
method lookup and the binding:

```csharp
global::VerificationGlue.Tests.TestRegistry.Add(new global::VerificationGlue.Tests.TestDescriptor(
    "Shop.Tests.OrderServiceTests.Create_stamps_the_order_with_the_clock(Shop.IClock,Shop.OrderService,Shop.IOrderStore)",
    ...
    static () => [new global::Shop.SutModuleAttribute()],
    static () => [new global::VerificationGlue.Tests.VerifyTestAttribute()],
    ...
    new global::VerificationGlue.Tests.TestParameter[]
    {
        new("clock", typeof(global::Shop.IClock), "Shop.IClock", 0, false, static () => [new global::VerificationGlue.Mocks.MockAttribute()], static () => global::VerificationGlue.Mocks.Mock.Of<global::Shop.IClock>(), null, false, null),
        new("sut", typeof(global::Shop.OrderService), "Shop.OrderService", 1, false, static () => [], null, null, false, null),
        new("store", typeof(global::Shop.IOrderStore), "Shop.IOrderStore", 2, false, static () => [], null, null, false, null),
    },
    ...,
    method: static () => typeof(global::Shop.Tests.OrderServiceTests).GetMethod(
        "Create_stamps_the_order_with_the_clock",
        global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.DeclaredOnly,
        new global::System.Type[] { typeof(global::Shop.IClock), typeof(global::Shop.OrderService), typeof(global::Shop.IOrderStore) })!,
    container: new global::VerificationGlue.Tests.TestContainerBinding(
        static context => global::VerificationGlue.Tests.Generated.DependencyModulesContainer.BuildAsync(context, null),
        supportsChildOverrides: true)));
```

The runner creates the `clock` mock and adds it as an override. The binding builds the container,
and `OrderService` receives the mock.

### Case 2: a module declared in the test project

```csharp
namespace Shop.Tests;

[DependencyModule]
public partial class TestDoublesModule
{
    // [SingletonService] classes in the test project, such as an InMemoryOrderStore
}

[SutModule]
[TestDoublesModule]
public partial class CheckoutTests
{
    [VerifyTest]
    public void Checkout_stores_the_order(OrderService sut, IOrderStore store) { ... }
}
```

`[TestDoublesModule]` binds to an error type in VG's generator. Part 1 resolves it to the class
`Shop.Tests.TestDoublesModule`, which carries `[DependencyModule]`. The class attributes become:

```csharp
static () => [new global::Shop.SutModuleAttribute(), new global::Shop.Tests.TestDoublesModuleAttribute()],
```

Today that second attribute is left out. The `method:` and `container:` arguments are the same as in
case 1. VGT0006 is not reported for the attribute.

### Case 3: `[Container<Module>]`

```csharp
[Container<SutModule>]
public partial class InventoryTests
{
    [VerifyTest]
    public void Reserve_lowers_the_stock(InventoryService sut) { ... }
}
```

```csharp
static () => [new global::VerificationGlue.Tests.ContainerAttribute<global::Shop.SutModule>()],
...
container: new global::VerificationGlue.Tests.TestContainerBinding(
    static context => global::VerificationGlue.Tests.Generated.DependencyModulesContainer.BuildAsync(
        context, static () => new global::Shop.SutModule()),
    supportsChildOverrides: true)
```

For a test-project module the expression is the same, with `new global::Shop.Tests.TestDoublesModule()`.
It compiles because the final compilation holds DM's partial that adds `IDependencyModule`.

### Case 4: `[Container<Bootstrap>]`

```csharp
[Container<ShopBootstrap>]
[SutModule]
public partial class OrderServiceTests { ... }
```

The explicit container wins (Part 7). The generator writes the bootstrap's build call. `[SutModule]`
is materialized as an attribute, and `ShopBootstrap` may read it from `context.Attributes`:

```csharp
static () => [new global::VerificationGlue.Tests.ContainerAttribute<global::Shop.Tests.ShopBootstrap>(), new global::Shop.SutModuleAttribute()],
...
container: new global::VerificationGlue.Tests.TestContainerBinding(
    static context => global::Shop.Tests.ShopBootstrap.BuildAsync(context),
    supportsChildOverrides: false)
```

No `method:` argument is written, because no DependencyModules container is built. A project
without DependencyModules gets exactly this for every `[Container<T>]`, which is today's behaviour
moved from the attribute into the descriptor.

### Case 5: a shared scope

```csharp
[Container<SutModule>(Scope = ContainerScope.Class)]
public partial class OrderHistoryTests
{
    [VerifyTest]
    public void Each_test_gets_its_own_mock([Mock] IClock clock, OrderService sut, [Shared] IOrderStore store) { ... }

    [VerifyTest]
    public void The_store_is_shared_by_the_class([Shared] IOrderStore store, OrderService sut) { ... }
}
```

```csharp
static () => [new global::VerificationGlue.Tests.ContainerAttribute<global::Shop.SutModule>() { Scope = (global::VerificationGlue.Tests.ContainerScope)(1) }],
...
new("store", typeof(global::Shop.IOrderStore), "Shop.IOrderStore", 2, false, static () => [new global::DependencyModules.Testing.Attributes.SharedAttribute()], null, null, false, null),
...
container: new global::VerificationGlue.Tests.TestContainerBinding(
    static context => global::VerificationGlue.Tests.Generated.DependencyModulesContainer.BuildAsync(
        context, static () => new global::Shop.SutModule()),
    supportsChildOverrides: true)
```

The runner keys the class container by `typeof(Shop.SutModule)`, as it keys a bootstrap today. The
first test of the class makes the runner call `Build` with the class scope's context. That context
has a null `Test`, so `BuildScopeAsync` builds the parent. For each test the runner calls
`CreateChildAsync`, and `BuildAsync` registers the parent's `IOrderStore` instance and then the
test's mock of `IClock`. Both tests see one store. Only the `OrderService` of
`Each_test_gets_its_own_mock` receives a mock clock.

### Case 6: `[TestExport]`

```csharp
[SutModule]
[TestExport(typeof(IClock), Implementation = typeof(FixedClock))]
public partial class InvoiceTests
{
    [VerifyTest]
    public void Uses_the_fixed_clock(InvoiceService sut) { ... }

    [VerifyTest]
    public void A_mock_beats_the_export([Mock] IClock clock, InvoiceService sut) { ... }
}
```

`[TestExport]` binds and materializes today. Nothing new is generated for it:

```csharp
static () => [new global::Shop.SutModuleAttribute(), new global::DependencyModules.Testing.Attributes.TestExportAttribute(typeof(global::Shop.IClock)) { Implementation = typeof(global::Shop.Tests.FixedClock) }],
```

The entry point runs it in step 5, after the modules, so `FixedClock` replaces the application's
clock. In the second test the mock is an override, registered in step 8, so the mock replaces
`FixedClock`. The `[TestExport]` is on the class and the `[Mock]` is on a method parameter, so
DM0021 does not report them. Moved onto the method, the `[TestExport]` would report DM0021.

### Case 7: a module with constructor arguments

```csharp
// In Shop
[DependencyModule]
public partial class RegionModule(string region)
{
    public bool UseSandbox { get; set; }
}

// In the test project
[RegionModule("eu", UseSandbox = true)]
public partial class TaxTests
{
    [VerifyTest]
    public void Applies_the_eu_rate(TaxService sut) { ... }
}

[Container<RegionModule>(Scope = ContainerScope.Class)]
[RegionModule("us")]
public partial class SharedTaxTests { ... }
```

The attribute carries the arguments, and DM's `GetModule()` passes them on:

```csharp
// TaxTests
static () => [new global::Shop.RegionModuleAttribute("eu") { UseSandbox = true }],
...
container: new global::VerificationGlue.Tests.TestContainerBinding(
    static context => global::VerificationGlue.Tests.Generated.DependencyModulesContainer.BuildAsync(context, null),
    supportsChildOverrides: true)

// SharedTaxTests: no container module, because RegionModule has no parameterless constructor.
static () => [new global::VerificationGlue.Tests.ContainerAttribute<global::Shop.RegionModule>() { Scope = (global::VerificationGlue.Tests.ContainerScope)(1) }, new global::Shop.RegionModuleAttribute("us")],
...
container: new global::VerificationGlue.Tests.TestContainerBinding(
    static context => global::VerificationGlue.Tests.Generated.DependencyModulesContainer.BuildAsync(context, null),
    supportsChildOverrides: true)
```

If `RegionModule` were declared in the test project, the generator would write the same
`RegionModuleAttribute("eu") { UseSandbox = true }` from the attribute syntax (Part 1).

`SharedTaxTests` without its `[RegionModule("us")]` reports VGT0009. So does a `[RegionModule("us")]`
placed only on a method, because the class parent cannot see it.

---

## New diagnostics

All three are VerificationGlue diagnostics in `VerificationGlue.Tests.Generator/Diagnostics.cs`,
reported by `TestAnalyzer`. Each gets a page in `docs/diagnostics`. DependencyModules gets no new
diagnostic. DM0021 changes as Part 8 describes.

| ID | Severity | Title | Reported when |
|---|---|---|---|
| VGT0009 | Error | The container of a test cannot be wired | `[Container<T>]` names a `T` that is neither a bootstrap nor a module. Or `T` is a module whose constructor needs arguments and no attribute for it is on a scope the container can see. Or a module container would need `Microsoft.Extensions.DependencyInjection` and it does not resolve. Or a class or assembly scope uses the Runtime mode. The message names which. |
| VGT0010 | Error | A test's container can be wired more than one way | Two recognizers claim a test that has no `[Container<T>]`. Or `T` is both an `ITestContainerBootstrap` and a module. |
| VGT0011 | Warning | DependencyModules.Testing is too old for VerificationGlue | `DependencyModules.Testing.Attributes.TestExportAttribute` resolves but `DependencyModules.Testing.Impl.ModuleTestComposition` does not. The message says that `[TestExport]`, `[InjectValues]`, environments and startup attributes are not applied, and names the version to update to. |

The runner keeps a runtime check behind VGT0009, as VGT0008 has one. A test with a
`ContainerAttribute` and no binding fails with a message that starts with `VGT0009:`.

---

## Changes, in the order they land

The dependency between the repositories runs one way. VerificationGlue's specs need a published
DependencyModules.Testing with the entry point. DependencyModules needs nothing from
VerificationGlue until its own VG test project, which is the last step.

1. **DependencyModules: the entry point.** Add `ModuleTestComposition`, `ModuleTestContainer`,
   `TestServiceOverride` and `TestMethodContext` to DependencyModules.Testing. Add its references to
   DependencyModules.Runtime and Microsoft.Extensions.DependencyInjection. Unit-test each step of
   Part 3 against a plain `ServiceCollection`, including the disposal of every container on a
   failure, step 6 with a parent, and the skipping of overridden parameters. Update
   `PublicApiTests.TestingApi.verified.txt`.
2. **DependencyModules: xUnit, xUnit4 and NUnit on the entry point.** The behaviour must not
   change. The check is the existing suites (DependencyModules.Tests, SutProject.Tests,
   SutProject.NUnitTests, WebApiApp.Tests) passing unchanged. This can share a release with 1, and
   must come after it.
3. **DependencyModules: DM0021 accepts `VerificationGlue.Mocks.MockAttribute`.** Add the name to
   `KnownTypes`, plus tests in `MockTestExportDiagnosticTests` with a stub of VG's attribute. Add a
   sentence to `MockAttribute`'s remarks. This is independent of 1 and 2.
4. **Release DependencyModules** with 1 to 3, as 1.8.0.
5. **VerificationGlue: container bindings.** Add `TestContainerBinding` and the `container:` and
   `method:` descriptor arguments. Add `TestContext.Current`, `TestClass`, `Method` and `IsRunning`,
   and `TestContainerContext.Test`. Remove the constraint of `ContainerAttribute<T>` and its
   internal `BuildAsync` (Part 2, option B). The generator writes `container:` for every bootstrap.
   The `GeneratedCodeSpecs` snapshots change. This needs no DependencyModules and can be built
   while 1 to 4 are under way.
6. **VerificationGlue: recognition and the Runtime mode.** Part 1, Part 4 in both generators,
   Part 7, VGT0009 and VGT0010, the VGT0006 change, and the Runtime-mode assembly file. The specs
   reference the DependencyModules.Runtime and DependencyModules.SourceGenerator packages as
   test-only dependencies. A spec runs DM's generator and VG's together to cover the test-project
   module and the analyzer-generator agreement. This needs a DependencyModules release that is
   already public, so it does not wait for step 4.
7. **VerificationGlue: the Testing mode.** The Testing-mode assembly file, Part 5, Part 6 and
   VGT0011. The specs reference DependencyModules.Testing 1.8.0. A sample in which a keyed `[Mock]`
   reaches the system under test, as `docs/design/tests.md` Phase 4 asks. An AOT publish of a
   sample that uses a module, to check the `method:` lookup. This needs step 4.
8. **VerificationGlue: documentation.** A guide page. The rewrite of "DependencyModules adapter" and
   Phase 4 in `docs/design/tests.md`. The diagnostic pages. The migration guide from
   `DependencyModules.xUnit`. Release VerificationGlue.
9. **DependencyModules: a VG test project and the docs.** Carry the build, run and coverage setup
   from the `agent/stp-vg-adapter` branch: `NuGet.config`, `scripts/test-verificationglue.sh` and
   the `coverage.sh` changes. Add `integ-tests/SutProject.VerificationGlueTests` without the adapter
   package. Add a `website/guide/testing-verificationglue.md` page beside `testing-xunit.md` and
   `testing-nunit.md`. This needs step 8.

Hardened.Framework needs no change to work in steps 7 and 8. Its own VG migration guide would follow
step 8.

---

## Open questions

1. **Should DependencyModules.Testing reference DependencyModules.Runtime and
   Microsoft.Extensions.DependencyInjection?** The alternative is an entry point that takes modules
   as `object` and a build delegate, which pushes both references onto every caller. Every caller
   has both already. The only packages that gain dependencies are the three mock packages, and
   their users run a runner package that has both. *Recommendation:* add the references, and remove
   the sentence in `IModuleEnvironmentProvider`'s remarks that says Testing does not reference
   Runtime.
2. **Is the generated `GetMethod` lookup acceptable in VerificationGlue?** VG's design promises no
   reflection. The lookup is written only in the Testing mode, and DependencyModules' interfaces
   take `MethodInfo` and `ParameterInfo` throughout. *Recommendation:* accept it for the Testing
   mode, document it as that mode's one reflection call, and check it in the AOT publish of step 7.
   The reflection-free variants of DM's test interfaces that `docs/design/tests.md` lists are a
   separate change and do not block this one.
3. **Should the shared parent run environment providers and startup attributes?** Both interfaces
   take a test method, and a parent has none. As designed, the parent decides `[IfEnvironment]`
   against the default environment while its children use the test's. A shared instance can
   therefore come from another environment than the test declares. *Recommendation:* ship without
   it, and say so on the guide page. If a user asks, add a default interface member
   `IModuleEnvironment? ProvideScopeEnvironment(IReadOnlyList<Attribute> scopeAttributes) => null`
   to `IModuleEnvironmentProvider`. That is not a breaking change.
4. **How is CS0104 on `[Mock]` resolved?** A file that imports both namespaces cannot write `[Mock]`
   without an alias. *Recommendation:* VerificationGlue.Tests' `build/VerificationGlue.Tests.props`
   adds `<Using Include="VerificationGlue.Mocks.MockAttribute" Alias="MockAttribute" />`, unless
   `VerificationGlueMockAlias` is `false`. A using alias is found before a type that a namespace
   import brings in, so `[Mock]` binds to VG's attribute in every file. The alias is harmless in a
   project without DependencyModules. A file that declares its own `MockAttribute` alias then fails
   with a duplicate-alias error, which is why the property exists. I have not compiled this.
5. **Should `[HardenedModule]` be a known module marker?** Hardened's generator writes module
   attributes for `[Hardened.Shared.Runtime.Attributes.HardenedModule]` classes too. A Hardened test
   names its application through `[HardenedTestEntryPoint]`, which binds, so it does not need this.
   *Recommendation:* not now. Adding a second marker name later changes nothing else.
6. **Should DependencyModules parameter attributes run on test-class constructor parameters?** VG
   supports constructor parameters, and DM's runners do not. As designed, a constructor parameter
   gets a registered service or a VG mock, and its `[InjectValues]` and other DM parameter
   attributes do not run. *Recommendation:* accept and document it. A constructor lookup like the
   method lookup would add it later.
7. **Is the Runtime mode worth having?** It serves a test project that references an application
   and no DependencyModules.Testing. It is a small generated class. Without it such a project gets
   VGT0009 or nothing. *Recommendation:* keep it, test-scoped only, as designed.
8. **Should `ProvideAsync` skip parameters that a data row supplies?** DM's `ResolveArgumentsAsync`
   runs no parameter provider for a row value. The generated container does not know the row count.
   A parameter with both a row value and a DM parameter attribute is unlikely. *Recommendation:* pass
   the row count in `TestContainerContext` only if a test needs it.

---

## How this was established

Read in this turn:

- In DependencyModules: `IDependencyModule.cs`, `IDependencyModuleProvider.cs`,
  `IModuleEnvironmentProvider.cs`, `ServiceCollectionExtensions.cs`,
  `DependencyModuleAttribute.cs`, the `ModuleWithAllServiceLifetimes` and
  `ModuleWithConstructorParametersAndProperties` snapshots, every file in
  `src/DependencyModules.Testing`, `ModuleTestCase.cs`, the build flow of NUnit's
  `ModuleTestCommand.cs`, `TestAttributeDiagnostics.cs`, DM0021 and DM0019 in
  `DependencyModuleDiagnostics.cs`, `MockTestExportDiagnosticTests.cs`, and the compilation-unit
  selection in `BaseSourceGenerator.cs`.
- In VerificationGlue: the test generator (`TestGenerator`, `TestDiscovery`, `TestEmitter`,
  `AttributeMaterializer`, `TestAnalyzer`, `KnownTypes`, `Diagnostics`, `Models`),
  `ContainerAttribute.cs`, `TestContainer.cs`, `ExtensionPoints.cs`, `TestParameter.cs`,
  `TestContext.cs`, `TestScope.cs`, the container part of `TestRunner.cs`, `MockDiscovery.cs`,
  `WebAppAttribute.cs`, the build props and targets, `docs/guide/containers-and-scopes.md`,
  `extending.md`, `test-parameters.md`, `generated/test-registrations.md`, VGT0008 and VGM0001,
  and `docs/design/tests.md`.
- In Hardened.Framework: `docs/guide/testing.md`, the outline of `testing-mocks.md`,
  `HardenedSourceGenerator.cs`, `EntryPointDiagnostics.cs`, the interfaces and the module, setup and
  logging parts of `HardenedTestEntryPointAttribute.cs`, `AttributeCollection.FromMethodInfo`, and
  which attributes implement `ITestParameterValueProvider` and `ITestContainerSource`.
- The description of #134.

Not checked: none of the generated code above has been compiled. I did not build a test-project
module under VG to see whether VGT0006 reports it today. I did not check the file name of MTP's
generated entry point, which decides whether DM0019 can report in a VG project. I did not check
whether the DM package docs tell users to set `PrivateAssets` on DependencyModules.SourceGenerator,
which decides whether DM0021 runs in a test project that declares no module.
