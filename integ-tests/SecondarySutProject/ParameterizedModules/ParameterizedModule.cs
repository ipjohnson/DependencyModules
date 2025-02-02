using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SecondarySutProject.ParameterizedModules;

[DependencyModule(OnlyRealm = true)]
public partial class ParameterizedModule : IServiceCollectionConfiguration {
    private readonly string _a;
    private readonly int _b;

    private ParameterizedModule(string a, int b) {
        _a = a;
        _b = b;
    }

    public string? C { get; set; }

    public void ConfigureServices(IServiceCollection services) {
        services.AddTransient<SomeRuntimeDependency>(_ => new SomeRuntimeDependency(_a, _b, C!));
    }
}