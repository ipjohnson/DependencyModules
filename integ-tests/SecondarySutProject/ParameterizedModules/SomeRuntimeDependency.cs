namespace SecondarySutProject.ParameterizedModules;

public class SomeRuntimeDependency {

    public SomeRuntimeDependency(string someDependency, int intDependency, string cValue) {
        SomeDependency = someDependency;
        IntDependency = intDependency;
        CValue = cValue;
    }

    public string SomeDependency { get; }

    public int IntDependency { get; }

    public string? CValue { get; }
}