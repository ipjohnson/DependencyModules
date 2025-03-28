using DependencyModules.Runtime.Attributes;
using Microsoft.Extensions.DependencyInjection;
using SutProject.Tests.KeyedTests;

namespace SutProject.Tests.GenerateFactories;

[DependencyModule(OnlyRealm = true, GenerateFactories = true)]
public partial class GenerateFactoryModule;

[SingletonService(Realm = typeof(GenerateFactoryModule))]
public class FactoryDepOne(
    ISingletonService? singletonService = null, 
    IScopedService? scopedService = null) : IDependencyOne {

    public ISingletonService SingletonService {
        get;
    } = singletonService!;

    public IScopedService ScopedService {
        get;
    } = scopedService!;
}

[SingletonService(Realm = typeof(GenerateFactoryModule), Key = "Keyed")]
public class GenerateKeyed() : IKeyedRegistration {
    public string Key {
        get;
    } = "Keyed";
}

[SingletonService(Realm = typeof(GenerateFactoryModule))]
public class KeyedDependency([FromKeyedServices("Keyed")] IKeyedRegistration registration) {
    public IKeyedRegistration Registration {
        get;
    } = registration;
}