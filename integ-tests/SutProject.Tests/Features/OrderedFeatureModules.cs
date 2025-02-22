using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Features;
using Microsoft.Extensions.DependencyInjection;

namespace SutProject.Tests.Features;

public interface IOrderedFeature {
    
}


[DependencyModule(OnlyRealm = true)]
public partial class FirstFeatureHandler : IDependencyModuleFeature<IOrderedFeature> {

    public int Order => 1;

    public void HandleFeature(IServiceCollection collection, IEnumerable<IOrderedFeature> feature) {
        collection.AddSingleton(_ => new DependencyValue("1"));        
    }
}

[DependencyModule(OnlyRealm = true)]
public partial class SecondFeatureHandler : IDependencyModuleFeature<IOrderedFeature> {

    public int Order => 2;

    public void HandleFeature(IServiceCollection collection, IEnumerable<IOrderedFeature> feature) {
        collection.AddSingleton(_ => new DependencyValue("2"));
    }
}

[DependencyModule(OnlyRealm = true)]
public partial class ThirdFeatureHandler : IDependencyModuleFeature<IOrderedFeature> {

    public int Order => 3;

    public void HandleFeature(IServiceCollection collection, IEnumerable<IOrderedFeature> feature) {
        collection.AddSingleton(_ => new DependencyValue("3"));
    }
}


