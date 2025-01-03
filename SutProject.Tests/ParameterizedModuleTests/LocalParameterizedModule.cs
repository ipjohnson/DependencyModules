using DependencyModules.Runtime.Attributes;
using SecondarySutProject.ParameterizedModules;

namespace SutProject.Tests.ParameterizedModuleTests;

[DependencyModule]
[ParameterizedModule.Module("local-string", 20)]
public partial class LocalParameterizedModule {
    
}