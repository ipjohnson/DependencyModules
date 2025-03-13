using DependencyModules.Runtime.Attributes;

namespace SutProject.Tests.StandardTests;

[DependencyModule]
[SutModule]
public partial class TestModule;

[DependencyModule]
[SutModule]
public partial class TestModule2;

[DependencyModule]
[TestModule]
[TestModule2]
public partial class CombinedModule;

[DependencyModule]
[TestModule]
[TestModule]
[TestModule2]
[TestModule2]
public partial class DuplicateModule;