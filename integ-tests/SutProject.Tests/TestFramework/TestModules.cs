using DependencyModules.Runtime.Attributes;

namespace SutProject.Tests.TestFramework;

[DependencyModule(OnlyRealm = true)]
public partial class AssemblyLevelModule { }


[DependencyModule(OnlyRealm = true)]
public partial class ClassLevelModule {
    
}

[DependencyModule(OnlyRealm = true)]
public partial class MethodLevelModule { }