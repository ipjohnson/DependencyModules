using DependencyModules.Runtime.Attributes;

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