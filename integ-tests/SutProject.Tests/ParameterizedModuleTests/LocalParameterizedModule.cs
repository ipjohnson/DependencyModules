using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using SecondarySutProject.ParameterizedModules;

namespace SutProject.Tests.ParameterizedModuleTests;

[DependencyModule]
[ParameterizedModule.Attribute("local-string", 20, C = "CValue")]
public partial class LocalParameterizedModule : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        
    }
}