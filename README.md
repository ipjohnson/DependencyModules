# DependencyModules

[![NuGet](https://img.shields.io/nuget/v/DependencyModules.Runtime.svg)](https://www.nuget.org/packages/DependencyModules.Runtime/)
[![build](https://github.com/ipjohnson/DependencyModules/actions/workflows/build-package.yaml/badge.svg)](https://github.com/ipjohnson/DependencyModules/actions/workflows/build-package.yaml)
[![coverage](https://raw.githubusercontent.com/ipjohnson/DependencyModules/badges/coverage.svg)](https://github.com/ipjohnson/DependencyModules/actions/workflows/build-package.yaml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE.txt)

DependencyModules is a C# source generator package that uses attributes to create
dependency injection registration modules. These modules can then be used to populate 
an IServiceCollection instance.

Registration code is generated at compile time, so there is no reflection or assembly
scanning at run time.

## Installation

```shell
dotnet add package DependencyModules.Runtime
dotnet add package DependencyModules.SourceGenerator
```

Requires .NET 8.0 or later. See [CHANGELOG.md](CHANGELOG.md) for release notes.

## Service Attributes 

* `[DependencyModule]` - used to attribute class that will become dependency module (must be partial)
* `[SingletonService]` - registers service as `AddSingleton`
* `[ScopedService]` - registers service as `AddScoped`
* `[TransientService]` - registers service as `AddTransient`
* `[CrossWireService]` - registers implementation and interfaces with the same lifetime

```csharp
// Registration example
[DependencyModule]
public partial class ApplicationModule;

// registers SomeClass implementation for ISomeService
[SingletonService]
public class SomeClass : ISomeService 
{
  public string SomeProp => "SomeString";
}

// registers OtherService implementation
[TransientService]
public class OtherService
{
  public OtherService(ISomeService service)
  { 
    SomeProp = service.SomeProp;
  }
  public string SomeProp { get; }
}
```

Note: `[DependencyModule]` is not required for [Top-level](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/program-structure/top-level-statements) statement applications.

Note: a `[DependencyModule]` class must be declared directly in a namespace, not nested inside
another type. A nested module generates a separate, detached class rather than completing the
partial declaration, so its registrations never run. Services registered with
`[SingletonService]` and friends may be nested freely.
## Container Instantiation

* `AddModule` - method adds root module to service collection
* `AddModules` - add a list of modules to the service collection

```csharp
// AddModule and AddModules are extension methods in the DependencyModules.Runtime namespace
using DependencyModules.Runtime;

var serviceCollection = new ServiceCollection();

serviceCollection.AddModule<ApplicationModule>();
// or
serviceCollection.AddModules(new ApplicationModule(), ...);

var provider = serviceCollection.BuildServiceProvider();

var service = provider.GetService<OtherService>();
```

Note: to avoid duplicate modules it's recommended to only call AddModule(s) once in an application and never inside a Module.
## Factories

Sometimes it's not possible to construct all types through normal registration.
Factories can be registered with a module using the registration attributes.

```csharp
public class SomeClass : ISomeInterface {
  public SomeClass(IDep one, IDepTwo two, DateTime dateTime) { ... }
  
  [SingletonService]
  public static ISomeInterface Factory(IDep one, IDepTwo two) {
    return new SomeClass(one, two, DateTime.Now());   
  }
}
```
## Module Re-use

DependencyModules creates an `Attribute` class that can be used to apply sub dependencies.

```csharp
// Modules can be re-used with the generated attributes
[DependencyModule]
[ApplicationModule]
public partial class AnotherModule;
```

## Parameters

Sometimes you want to provide extra registration for your module. 
This can be achieved by adding a constructor to your module or optional properties. 
Note these parameters and properties will be correspondingly implemented in the module attribute.

```csharp
[DependencyModule]
public partial class SomeModule(bool someFlag) : IServiceCollectionConfiguration 
{
  public string OptionalString { get; set; } = "";
  
  public void ConfigureServices(IServiceCollection services) 
  {
    if (someFlag) 
    {
      // custom registration
    } 
  }
}

[DependencyModule]
[SomeModule(true, OptionalString = "otherString")]
public partial class SomeOtherModule;
```

## Module Features
Because module configuration happens before the dependency injection container is instantiated it's impossible to use the container for configuration.
To support configuration discovery before registration, the feature interface can be 
implemented in modules and be passed to a handler at registration time. Features are applied before services and decorators.

```csharp
// feature interface
public interface IFeature { }

[DependencyModule]
public partial class ModuleImplementation : ISomeFeature
{
}

[DependencyModule]
[ModuleImplementation]
public partial class FeatureHandlerModule : IDependencyModuleFeature<ISomeFeature> 
{
  public void HandleFeature(IServiceCollection collection, IEnumerable<ISomeFeature> features) 
  {
      // invoked with service collection and one instance of the ModuleImplementation class
  }
}
```

## Managing duplicate registration

By default a module will only be loaded once, assuming attributes are used or the modules are specified in the same `AddModules` call. Separate calls to `AddModule` will result in modules being loaded multiple times. If a module uses parameters it can be useful to load a module more than once. That can be accomplished by overriding the `Equals` and `GetHashcode` methods to allow for multiple loads.

```csharp
// CustomModule will be loaded as long as someString is unique.
// Duplicate modules with the same someString value will be ignored
[DependencyModule]
public partial class CustomModule(string someString) : IServiceCollectionConfiguration 
{
  public void ConfigureServices(IServiceCollection services) 
  {
    // custom logic
  }

  public override bool Equals(object obj)
  {
    if (obj is CustomModule module)
    {
      return someString.Equals(module.someString);
    }

    return false;
  }

  public override int GetHashCode()
  {
    return someString.GetHashCode();
  }
}
```

Services will be registered using an `Add` method by default. This can be overridden with the `Using` property on individual service or at the `DependencyModule` level. Note: the following are valid registration types Add, Try, TryEnumerable, Replace.

```csharp
[SingletonService(Using = RegistrationType.Try)]
public class SomeService;

[DependencyModule(Using = RegistrationType.Try)]
public partial class SomeModule;
```

## Realm

By default, all dependencies are registered in all modules within the same assembly. 
The realm allows the developer to scope down the registration within a given module.

```csharp
// register only dependencies specifically marked for this realm
[DependencyModule(OnlyRealm = true)]
public partial class AnotherModule;

[SingletonService(Realm = typeof(AnotherModule))]
public class SomeDep : ISomeInterface { }
```

## Keyed Registration

Registration attributes have a `Key` property that allows for specifying the key at registration time.

```csharp
[SingletonService(Key = "SomeKey")]
public class KeyService : IKeyService { }

// yields this registration line
services.AddKeyedSingleton(typeof(IKeyService), "SomeKey", typeof(KeyService));
```

## As Registration

Sometimes it's useful to register a type with a specific type vs. letting auto-registration pick a type.
The `As` property allows you to control the service type for the registration.

```csharp
[SingletonService(As = typeof(ISomeOtherInterface))]
public class KeyService : IKeyService, ISomeOtherInterface { }

// yields this registration line
services.AddSingleton<ISomeOtherInterface>(typeof(KeyService));
```

## Try, Replace, TryEnumerable

By default registrations are done using a standard `Add___` method.
It can be useful to change the registration to `Try`, `Replace`, and `TryEnumerable` with the `Using` property.

```csharp
[SingletonService(Using = RegistrationType.Try)]
public class KeyService : IKeyService { }

// yields this registration line
services.TryAddSingleton(typeof(IKeyService), typeof(KeyService));
```

## Autogenerated Modules

To simplify registration for [Top-Level](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/program-structure/top-level-statements) statement applications, an `ApplicationModule` will be autogenerated for file named Program.cs.

```csharp
[assembly: SomeOtherModule]

var serviceCollection = new ServiceCollection();

// load SomeOtherModule as well as all registrations in the current project
serviceCollection.AddModule<ApplicationModule>();
```

## Unit testing & Mocking

DependencyModules provides an xUnit extension to make testing much easier. 
It handles the population and construction of a service provider using specified modules.

```csharp
> dotnet add package DependencyModules.xUnit
> dotnet add package DependencyModules.xUnit.NSubstitute

// applies module & nsubstitute support to all tests.
// test attributes can be applied at the assembly, class, and test method level
[assembly: MyModule]
[assembly: NSubstituteSupport]

public class OtherServiceTests 
{
  [ModuleTest]
  public void SomeTest(OtherService test, [Mock]ISomeService service)
  {
     service.SomeProp.Returns("some mock value");
     Assert.Equals("some mock value", test.SomeProp);
  }
}


## Reporting a problem

If services are not being registered as you expect, these three steps produce almost everything
needed to diagnose it:

1. **Look at the generated code.** Set `<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>`
   and read the files under `obj/`. The registrations the generator produced are the ground truth.
2. **Turn on the generator log**, which records the configuration in effect, every module and
   service discovered, and anything skipped along with the reason:
   ```xml
   <PropertyGroup>
     <DependencyModules_LogOutputDirectory>$(MSBuildProjectDirectory)/dmlogs</DependencyModules_LogOutputDirectory>
   </PropertyGroup>
   ```
3. **Check for `DM####` warnings** in the build output. The generator reports these for mistakes it
   can detect, such as a service type that cannot be constructed or a module missing `partial`.

Please include the log and the generated file in any [issue](https://github.com/ipjohnson/DependencyModules/issues).


```
## Implementation

Behind the scenes the library generates registration code that can be used with any `IServiceCollection` compatible DI container.

Example generated code for [SutModule.cs](integ-tests/SutProject/SutModule.cs)
```csharp
    // SutModule.Dependencies.g.cs
    public partial class SutModule
    {
        [DynamicDependency(nameof(ModuleDependencies))]
        private static int moduleField = global::DependencyModules.Runtime.Helpers.DependencyRegistry<global::SutProject.SutModule>.Add(ModuleDependencies);

        private static void ModuleDependencies(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)
        {
            services.AddTransient(
                typeof(global::SutProject.IDependencyOne), 
                typeof(global::SutProject.DependencyOne)
            );
            services.AddSingleton(
                typeof(global::SutProject.IGenericInterface<>), 
                typeof(global::SutProject.GenericClass<>)
            );
            services.AddKeyedTransient(
                typeof(global::SutProject.KeyedService), 
                Constants.StringValue, 
                typeof(global::SutProject.KeyedService)
            );
            services.AddScoped(
                typeof(global::SutProject.IScopedService), 
                typeof(global::SutProject.ScopedService)
            );
            services.AddSingleton(
                typeof(global::SutProject.ISingletonService), 
                typeof(global::SutProject.SingletonService)
            );
            services.AddSingleton(
                typeof(global::SutProject.IGenericInterface<string>), 
                typeof(global::SutProject.StringGeneric)
            );
        }
    }

    // SutModule.Modules.g.cs
namespace SutProject
{
        #nullable enable
    public partial class SutModule : global::DependencyModules.Runtime.Interfaces.IDependencyModule
    {

        static SutModule()
        {
        }

        public void PopulateServiceCollection(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)
        {
            global::DependencyModules.Runtime.Helpers.DependencyRegistry<global::SutProject.SutModule>.LoadModules(services, this);
        }

        [Browsable(false)]
        void global::DependencyModules.Runtime.Interfaces.IDependencyModule.InternalApplyServices(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)
        {
            global::DependencyModules.Runtime.Helpers.DependencyRegistry<global::SutProject.SutModule>.ApplyServices(services);
        }

        [Browsable(false)]
        global::System.Collections.Generic.IEnumerable<object> global::DependencyModules.Runtime.Interfaces.IDependencyModule.InternalGetModules()
        {
            return global::DependencyModules.Runtime.Helpers.DependencyRegistry<global::SutProject.SutModule>.GetModules();
        }

        public override bool Equals(object? obj)
        {
            return obj is SutModule;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(base.GetHashCode());
        }
    }
    #nullable disable

    [global::System.AttributeUsage(global::System.AttributeTargets.Class | global::System.AttributeTargets.Assembly | global::System.AttributeTargets.Method | global::System.AttributeTargets.Parameter, AllowMultiple = true)]
    #nullable enable
    public partial class SutModuleAttribute : global::System.Attribute, global::DependencyModules.Runtime.Interfaces.IDependencyModuleProvider
    {

        public global::DependencyModules.Runtime.Interfaces.IDependencyModule GetModule()
        {
            var newModule = new global::SutProject.SutModule();
            return newModule;
        }
    }
    #nullable disable
}
```
