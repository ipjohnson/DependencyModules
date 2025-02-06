using DependencyModules.Runtime.Attributes;

namespace SutProject.Tests.StandardTests;

[DependencyModule]
[SutModule.Attribute]
public partial class TestModule { }

[DependencyModule]
[SutModule.Attribute]
public  partial class TestModule2 { }

[DependencyModule]
[TestModule.Attribute]
[TestModule2.Attribute]
public partial class CombinedModule { }

[DependencyModule]
[TestModule.Attribute]
[TestModule.Attribute]
[TestModule2.Attribute]
[TestModule2.Attribute]
public partial class DuplicateModule { }