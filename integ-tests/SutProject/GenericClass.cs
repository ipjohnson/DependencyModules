using DependencyModules.Runtime.Attributes;

namespace SutProject;

public interface IGenericInterface<T> {
    T Value { get; }
}

[SingletonService]
public class GenericClass<T>(T value) : IGenericInterface<T> {
    public T Value {
        get;
    } = value;
}

[SingletonService]
public class StringGeneric : IGenericInterface<string> {
    
    public string Value => "Hello World";
}