namespace SecondarySutProject.ParameterizedModules;

public class SomeRuntimeDependency {
    
    public SomeRuntimeDependency(string someDependency, int intDependency) {
        SomeDependency = someDependency;
        IntDependency = intDependency;
    }

    public string SomeDependency { get; }

    public int IntDependency { get; }
}