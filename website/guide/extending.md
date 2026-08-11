# Writing your own generator

## The problem

You want a registration mechanism this library does not have — your own attribute, a DSL that suits
your domain, registrations derived from something only your codebase knows about.

Writing that as a standalone source generator means rebuilding a lot of unglamorous machinery first:
finding the modules, parsing the MSBuild configuration, producing diagnostics, keeping the
incremental cache honest, and emitting registration code that composes with everything else. None of
that is the part you actually wanted to write.

## How DependencyModules helps

All of it lives in a shared assembly you can compile into your own analyzer. Your mechanism produces
the same `ServiceModel`s the attribute path produces, so emission needs no special case and your
registrations compose with `[SingletonService]` and conventions as if they had always been there.

The [convention](/guide/conventions) generator is exactly this — a registration mechanism of its own,
plugged into the same pipeline — and it is the worked example throughout this page. It ships inside
`DependencyModules.SourceGenerator` rather than beside it, but nothing about how it plugs in depends
on that; yours can live in its own analyzer package.

::: warning Not a stable public API yet
These are the extension points the convention generator uses, and they are public. They are **not**
versioned as a stable API, so a minor release may move them. If you build on this, pin the generator
package version.
:::

## What you get to reuse

| | |
|---|---|
| Module discovery | which `[DependencyModule]` classes exist, and their realms and features |
| Configuration | the `DependencyModules_*` MSBuild properties, already parsed |
| `DependencyFileWriter` | turns `ServiceModel`s into registration code |
| `FileLogger` | the diagnostic log users attach to issues |
| Diagnostics | the `DM####` descriptors and their release tracking |
| Model equality helpers | what keeps the incremental cache working |

## The shape

Two interfaces. `BaseSourceGenerator` is the Roslyn entry point, and it asks you for the generators
that want module models:

```csharp
[Generator]
public class MySourceGenerator : BaseSourceGenerator {

    protected override IEnumerable<IDependencyModuleSourceGenerator> AttributeSourceGenerators() {
        yield return new MyGenerator();
    }

    // SetupRootGenerator is deliberately not overridden. DependencyModules.SourceGenerator owns the
    // module partial; emitting it from here too would declare every module twice. The base class
    // knows that from the attribute you trigger on, so the default does the right thing here.
}
```

A generator that declares its **own** module attribute is the other shape, and the default flips to
match: nothing else can write those modules, so the base class writes them for you.

```csharp
[Generator]
public class MyFrameworkGenerator : BaseSourceGenerator {

    protected override ITypeDefinition[] ModuleAttributeTypes() =>
        [TypeDefinition.Get("My.Framework", "MyModuleAttribute")];

    protected override IEnumerable<IDependencyModuleSourceGenerator> AttributeSourceGenerators() {
        yield return new MyGenerator();
    }
}
```

`[MyModule]` on a class now gets everything `[DependencyModule]` does — `AddModule<T>()`, services,
conventions, decorators, interception — with no `[DependencyModule]` in the consuming project. Two
things follow from declaring your own attribute:

- **Override `SetupRootGenerator` with an empty body** if you want the attribute as a marker only,
  and no module written for it.
- **`Program.cs` is not yours.** A file of top level statements carries no attribute to tell the two
  generators apart, so the generated `ApplicationModule` belongs to whichever generator reads
  `[DependencyModule]`. If your framework ships without this package's generator and you want that
  module, override `ShouldAutoApproveCompilationUnit` to `true`.

`IDependencyModuleSourceGenerator` is one method. You receive the initialization context and a
provider of every discovered module paired with the configuration in effect:

```csharp
public class MyGenerator : IDependencyModuleSourceGenerator {

    public void SetupGenerator(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<(ModuleEntryPointModel Left, DependencyModuleConfigurationModel Right)> modules) {

        var candidates = context.SyntaxProvider
            .CreateSyntaxProvider(IsCandidate, GetModel)
            .Where(model => !model.IsIgnored)
            .Collect();

        context.RegisterSourceOutput(modules.Collect().Combine(candidates), Generate);
    }
}
```

For an attribute-driven mechanism, `BaseAttributeSourceGenerator<TModel>` does more of the work — you
supply the attribute types, a transform, a comparer and an ignored sentinel:

```csharp
public class MyGenerator : BaseAttributeSourceGenerator<MyModel> {
    protected override IEnumerable<ITypeDefinition> AttributeTypes() => [MyAttributeType];
    protected override MyModel GenerateAttributeModel(GeneratorAttributeSyntaxContext c, CancellationToken t) => …;
    protected override IEqualityComparer<MyModel> GetComparer() => new MyModelComparer();
    protected override MyModel IgnoredModel => MyModel.Ignore;
    protected override void GenerateSourceOutput(SourceProductionContext context, …) => …;
}
```

## Emitting registrations

Build `ServiceModel`s and hand them to `DependencyFileWriter`. The `uniqueId` becomes part of the
generated method and field names, so pick something that will not collide with another generator
contributing to the same module:

```csharp
var writer = new DependencyFileWriter(logger, coverageAttributeOnMethod: true);

var output = writer.Write(entryPointModel, configurationModel, serviceModels, "MyMechanism");

context.AddSource(
    entryPointModel.EntryPointType.GetFileNameHint(configuration.RootNamespace, "MyDependencies"),
    output);
```

::: tip coverageAttributeOnMethod
`[ExcludeFromCodeCoverage]` is not `AllowMultiple`, and attributes on partial parts combine. Only one
writer can own the class-level attribute, so every other file contributing to the same partial has to
apply it per member. Pass `true` unless you are the first.
:::

## Packaging

The project is an analyzer, and analyzer packaging is unforgiving in ways that only surface once
someone installs the package. Copy the conventions project's csproj rather than working it out again.

```xml
<PropertyGroup>
  <TargetFramework>netstandard2.0</TargetFramework>
  <IsRoslynComponent>true</IsRoslynComponent>
  <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
  <DevelopmentDependency>true</DevelopmentDependency>
  <IncludeBuildOutput>false</IncludeBuildOutput>
  <!-- Analyzer package: ships an analyzer and no lib/, which is what NU5128 flags. -->
  <NoWarn>$(NoWarn);NU5128</NoWarn>
</PropertyGroup>

<ItemGroup>
  <None Include="$(OutputPath)\$(AssemblyName).dll"
        Pack="true" PackagePath="analyzers/dotnet/cs" Visible="false"/>
</ItemGroup>
```

Roslyn supplies the compiler assemblies at load time, so every compiler dependency must be
`PrivateAssets="all"` or it leaks into your consumers' dependency graphs.

### Reusing the shared sources

The shared code is compiled **into** your analyzer rather than referenced, because an analyzer
assembly cannot depend on another one at load time:

```xml
<ItemGroup>
  <Compile Include="../DependencyModules.SourceGenerator.Impl/**/*.cs"
           Exclude="../DependencyModules.SourceGenerator.Impl/obj/**/*">
    <Link>Impl\%(RecursiveDir)/%(FileName)%(Extension)</Link>
  </Compile>
</ItemGroup>
```

That works because **`Impl` declares no `[Generator]` of its own**. Compiling it into a second
analyzer assembly adds no second registration of the service, decorator or interceptor generators, so
a project referencing both packages does not generate everything twice.

If you carry the `DM####` descriptors, you need their release tracking too, or the build fails
RS2008:

```xml
<ItemGroup>
  <AdditionalFiles Include="../DependencyModules.SourceGenerator.Impl/AnalyzerReleases.Shipped.md"/>
  <AdditionalFiles Include="../DependencyModules.SourceGenerator.Impl/AnalyzerReleases.Unshipped.md"/>
</ItemGroup>
```

## Three rules that will cost you a day each

**Never put a symbol in a model.** `ISymbol` is not equatable and holds its `SyntaxTree` alive. A
model containing one never compares equal across runs, so the incremental cache misses on every
keystroke and pins memory. Render what you need to strings or `ITypeDefinition` during the transform.

**Give every model structural equality.** A positional record compares `IReadOnlyList` members by
reference, so two structurally identical models built on consecutive runs are unequal and everything
downstream recomputes. `ModelEquality.ListEquals` and `ListHashCode` exist for this.

**Keep the predicate syntax-only and cheap.** It runs on a great many nodes. Reject on node type
first, and never touch the semantic model — resolve in the transform, which runs only for what the
predicate accepted.

## Refuse rather than guess

The house style is that an unsupported shape produces a `DM####` diagnostic and generates nothing.
The failure mode should be "this library does not support X", never a `CS` error inside generated
code, and never a silent absence.

Silent failure is the recurring bug class here. When you add something, ask what happens when it does
*not* work — and if the answer is "nothing is registered and the build is green", add a diagnostic.

## Testing it

Drive the generator in memory and then **execute what it produced**. Asserting on generated text
passes happily while the wrong service type is registered.

The pattern used throughout this repository is: compile the source with the generator, emit a real
assembly, load it, build a provider, and resolve. See [Testing modules](/guide/testing) for the
consumer-facing equivalent.

One caveat if you drive two analyzers from one test project: both compile in the shared `Impl`
sources, so referencing both as libraries puts two copies of every `Impl` type in scope and every use
is `CS0433`. Reach the second through an `Alias` and let everything else resolve to the first.
