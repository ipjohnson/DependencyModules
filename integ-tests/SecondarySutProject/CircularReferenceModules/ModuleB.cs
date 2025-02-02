using DependencyModules.Runtime.Attributes;

namespace SecondarySutProject.CircularReferenceModules;

[DependencyModule]
[ModuleA.Attribute]
public partial class ModuleB {
}