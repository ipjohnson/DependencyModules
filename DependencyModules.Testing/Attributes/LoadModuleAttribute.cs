using System.Reflection;

namespace DependencyModules.Testing.Attributes;

/// <summary>
/// Attribute used to tell the testing framework which models to use during the test.
/// Can be applied to Method, Class, And Assembly. Note attribute order can only be saved
/// between assembly, class and method. Order within assembly, class and method attributes is non-deterministic
/// </summary>
[AttributeUsage(
    AttributeTargets.Assembly | 
    AttributeTargets.Class | 
    AttributeTargets.Method,
    AllowMultiple = true)]
public class LoadModulesAttribute : Attribute {

    public LoadModulesAttribute(params Type[] moduleTypes) {
        ModuleType = moduleTypes;
        Parameters = [];
    }

    public LoadModulesAttribute(Type moduleType, object[] parameters) {
        ModuleType = [moduleType];
        Parameters = parameters;
    }
    
    public Type[] ModuleType {
        get;
    }

    public object[] Parameters { get; }
}