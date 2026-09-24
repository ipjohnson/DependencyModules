# Extending

This page tells you about the extension points of the runtime, of `DependencyModules.Testing`, and of the generator.

## Module features

A feature lets one module get the other loaded modules that implement an interface. Implement `IDependencyModuleFeature<TFeature>` from `DependencyModules.Runtime.Features` on a module:

```csharp
using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Plugins;

public interface IPluginModule
{
    string PluginName { get; }
}

public record PluginInfo(string Name);

[DependencyModule(OnlyRealm = true)]
public partial class PluginRegistryModule : IDependencyModuleFeature<IPluginModule>
{
    public void HandleFeature(IServiceCollection collection, IEnumerable<IPluginModule> feature)
    {
        foreach (var plugin in feature)
        {
            collection.AddSingleton(new PluginInfo(plugin.PluginName));
        }
    }
}

[DependencyModule(OnlyRealm = true)]
public partial class ReportsPluginModule : IPluginModule
{
    public string PluginName => "reports";
}
```

```csharp
using DependencyModules.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Plugins;

var services = new ServiceCollection();

services.AddModules(new PluginRegistryModule(), new ReportsPluginModule());

var plugins = services.BuildServiceProvider().GetServices<PluginInfo>();
```

When the modules load, `HandleFeature` gets each loaded module that implements `TFeature`. The features are applicable before the modules add their services. If more than one module implements a feature, the `Order` property sets the sequence of the feature handlers. The default value is 0.

Put `IDependencyModuleFeature<TFeature>` on the declaration of the module that has `[DependencyModule]`. The generator does not read the other partial declarations.

## Attributes that load modules

An attribute that implements `IDependencyModuleProvider` from `DependencyModules.Runtime.Interfaces` can load a module. The generated module attributes implement this interface. You can also write such an attribute:

```csharp
using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;

namespace Plugins;

[DependencyModule(OnlyRealm = true)]
public partial class NamedPluginModule(string name) : IPluginModule
{
    public string PluginName => name;
}

public class PluginAttribute(string name) : Attribute, IDependencyModuleProvider
{
    public IDependencyModule GetModule() => new NamedPluginModule(name);
}

[DependencyModule]
[Plugin("audit")]
public partial class HostModule;
```

When `HostModule` loads, `NamedPluginModule` also loads. The test packages also read these attributes on test methods, test classes, and the assembly.

The generated module attribute is `partial`. To add an interface to it, write a partial declaration:

```csharp
namespace Plugins;

public interface IDocumentedModule;

public partial class ReportsPluginModuleAttribute : IDocumentedModule;
```

## Test extension points

`DependencyModules.Testing` has interfaces for attributes that change the steps of a test. For more information, refer to [Attributes that you write for tests](./testing.md#attributes-that-you-write-for-tests) and [Other mock libraries](./testing-mocking.md#other-mock-libraries).

## A source generator for different framework attributes

A framework can use a different attribute to identify a module. It can also use different service attributes. The `DependencyModules.SourceGenerator.Impl` package contains the source code of the generator. You compile this source code into your generator.

Do these steps:

1. Make a class library that has the target framework `netstandard2.0`.
2. Add the `DependencyModules.SourceGenerator.Impl` package.
3. Set `PackageDependencyModuleIncludeSource` to `true`.
4. Write a class that derives from `BaseSourceGenerator`.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsRoslynComponent>true</IsRoslynComponent>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <PackageDependencyModuleIncludeSource>true</PackageDependencyModuleIncludeSource>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="DependencyModules.SourceGenerator.Impl" Version="1.6.0" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

The generator source code uses the `CSharpAuthor` package. The `DependencyModules.SourceGenerator.Impl` package contains the source code of `CSharpAuthor`, and the project compiles it with the generator source code. If the project sets `PackageCSharpAuthorIncludeSource` to `true`, the project compiles `CSharpAuthor` from its own `CSharpAuthor` package. The project then does not compile the copy.

Version 1.5.0 and the versions before it do not contain the source code of `CSharpAuthor`. For these versions, also add the `CSharpAuthor` package, version 2.0.0, with `IncludeAssets="build"`. Then set `PackageCSharpAuthorIncludeSource` to `true`.

```csharp
using CSharpAuthor;
using DependencyModules.Conventions;
using DependencyModules.SourceGenerator;
using DependencyModules.SourceGenerator.Impl;
using Microsoft.CodeAnalysis;

namespace MyFramework.Generator;

[Generator]
public class FrameworkGenerator : BaseSourceGenerator
{
    protected override ITypeDefinition[] ModuleAttributeTypes() =>
        new[] { TypeDefinition.Get("MyFramework", "FrameworkModuleAttribute") };

    protected override IEnumerable<IDependencyModuleSourceGenerator> AttributeSourceGenerators()
    {
        yield return new ServiceSourceGenerator();
        yield return new ConventionGenerator();
    }
}
```

The source code in the package declares no `[Generator]` class. Only the `DependencyModules.SourceGenerator` package declares one. Thus your generator does not contain a copy of the DependencyModules generator.

`BaseSourceGenerator` has these members to override:

| Member | Function |
| --- | --- |
| `ModuleAttributeTypes()` | The attributes that identify a module. The default is `[DependencyModule]`. If your generator uses only `[DependencyModule]`, it does not write the module class. The `DependencyModules.SourceGenerator` package writes it. |
| `AttributeSourceGenerators()` | The parts that write registrations. |
| `SetupRootGenerator(...)` | Writes the module classes. An override can write no module classes. |
| `ShouldAutoApproveCompilationUnit` | If the value is `true`, the generator uses `Program.cs` as the entry of a generated `ApplicationModule`. The default value is `true` only if `ModuleAttributeTypes()` gives only `[DependencyModule]`. |
| `GenerateEntryPointModel(...)` | Makes the model of a module from a module declaration or from `Program.cs`. |

A framework generator that does not use `DependencyModules.SourceGenerator` can set `ShouldAutoApproveCompilationUnit` to `true`. The framework generator then writes the `ApplicationModule`.

The package contains these parts:

- `ServiceSourceGenerator` writes the registrations for the service attributes.
- `ConventionGenerator` writes the convention registrations and the decorators.

The package does not contain the interception part.

A part implements `IDependencyModuleSourceGenerator`. This interface has one method: `SetupGenerator(context, provider)`. The provider gives pairs of `ModuleEntryPointModel` and `DependencyModuleConfigurationModel` values.

To write registrations for attributes that you declare, derive a part from `BaseAttributeSourceGenerator<TModel>`. Implement `AttributeTypes()`, `GenerateAttributeModel`, `GenerateSourceOutput`, `GetComparer()`, and `IgnoredModel`.

`DependencyFileWriter` writes the registration code for a list of `ServiceModel` values. Its `Write` method has a `uniqueId` parameter. The name of the generated method is `uniqueId` and the suffix `Dependencies`. Thus each part that adds registrations to a module must use a different `uniqueId`.

`DependencyFileWriter` puts `[ExcludeFromCodeCoverage]` on the members that it writes, and not on the class. Use the constructor that takes only the logger. The constructor with the `coverageAttributeOnMethod` parameter is obsolete. It ignores the value of the parameter.

The generator source code declares the DM diagnostics. The compiler then gives the warning RS2008 for each diagnostic, because your project has no analyzer release tracking.

To suppress these warnings, add `RS2008` to `NoWarn`. You can also add release tracking files.

A project that uses your generator must reference `DependencyModules.Runtime`, because the generated code uses it.

Your generator reads the MSBuild properties only if the project declares them as `CompilerVisibleProperty` items. The `DependencyModules.SourceGenerator` package declares them in its `build` folder. Declare them in your package too.

The `DependencyModules.SourceGenerator` package has these properties. You can use the same properties for your generator package:

- `IncludeBuildOutput` is `false`.
- `DevelopmentDependency` is `true`.
- The generator assembly is in the `analyzers/dotnet/cs` folder of the package.
- `NoWarn` contains `NU5128`.
- The Roslyn package references have `PrivateAssets="all"`.
