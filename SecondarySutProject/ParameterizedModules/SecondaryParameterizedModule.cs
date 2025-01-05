using DependencyModules.Runtime.Attributes;

namespace SecondarySutProject.ParameterizedModules;

[DependencyModule(OnlyRealm = true)]
[ParameterizedModule.Module("test-string", 10)]
public partial class SecondaryParameterizedModule { }