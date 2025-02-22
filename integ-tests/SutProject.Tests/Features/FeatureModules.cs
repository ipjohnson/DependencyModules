using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Features;
using Microsoft.Extensions.DependencyInjection;

namespace SutProject.Tests.Features;

public interface IModuleFeatureValue {
    string Value { get; }
}

[DependencyModule(OnlyRealm = true)]
public partial class FeatureModuleA : IModuleFeatureValue {

    public string Value => "A";
}

[DependencyModule(OnlyRealm = true)]
public partial class FeatureModuleB : IModuleFeatureValue {

    public string Value => "B";
}

[DependencyModule(OnlyRealm = true)]
public partial class FeatureModuleC : IModuleFeatureValue {

    public string Value => "C";
}

public class DependencyValue(string value) {
    public string Value { get; } = value;
}

[DependencyModule(OnlyRealm = true)]
public partial class FeatureModuleHandler : IDependencyModuleFeature<IModuleFeatureValue> {

    public void HandleFeature(IServiceCollection collection, IEnumerable<IModuleFeatureValue> feature) {
        foreach (var featureValue in feature) {
            collection.AddSingleton(_ => new DependencyValue(featureValue.Value));
        }
    }
}