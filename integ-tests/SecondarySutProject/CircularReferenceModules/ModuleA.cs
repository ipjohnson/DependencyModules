using DependencyModules.Runtime.Attributes;

namespace SecondarySutProject.CircularReferenceModules;

[DependencyModule]
[ModuleB.Attribute]
public partial class ModuleA { }