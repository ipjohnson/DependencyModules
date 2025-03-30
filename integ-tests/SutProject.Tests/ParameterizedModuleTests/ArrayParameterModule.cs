using DependencyModules.Runtime.Attributes;

namespace SutProject.Tests.ParameterizedModuleTests;

[DependencyModule]
public partial class ArrayParameterModule {
    public string[]? ArrayParameter { get; set; } = [];
    
    public Type? TypeValue { get; set; }
}


[DependencyModule]
[ArrayParameterModule(ArrayParameter = ["A", "B"], TypeValue = typeof(int))]
public partial class AnotherModule {
    
}