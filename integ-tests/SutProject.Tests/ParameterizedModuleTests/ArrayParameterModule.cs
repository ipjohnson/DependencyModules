using DependencyModules.Runtime.Attributes;

namespace SutProject.Tests.ParameterizedModuleTests;

[DependencyModule]
public partial class ArrayParameterModule {
    public string[]? ArrayParameter { get; set; } = [];
    
    public Type? TypeValue { get; set; }

    // The other answer DM0018 accepts: this fixture is composed once, so type-only identity is
    // what is wanted. Declaring it says so, rather than leaving the generator to assume it.
    public override bool Equals(object? obj) => obj is ArrayParameterModule;

    public override int GetHashCode() => typeof(ArrayParameterModule).GetHashCode();
}


[DependencyModule]
[ArrayParameterModule(ArrayParameter = ["A", "B"], TypeValue = typeof(int))]
public partial class AnotherModule {
    
}