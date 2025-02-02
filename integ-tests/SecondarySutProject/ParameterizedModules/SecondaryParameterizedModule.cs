using DependencyModules.Runtime.Attributes;

namespace SecondarySutProject.ParameterizedModules;

[DependencyModule(OnlyRealm = true)]
[ParameterizedModule.Attribute("test-string", 10)]
public partial class SecondaryParameterizedModule { }