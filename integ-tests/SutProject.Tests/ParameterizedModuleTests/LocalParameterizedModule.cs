using DependencyModules.Runtime.Attributes;
using SecondarySutProject.ParameterizedModules;

namespace SutProject.Tests.ParameterizedModuleTests;

[DependencyModule]
[ParameterizedModule.Attribute("local-string", 20, C = "CValue")]
public partial class LocalParameterizedModule { }