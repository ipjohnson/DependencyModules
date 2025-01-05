using DependencyModules.Runtime.Attributes;

namespace SutProject;

public interface IGenericInterface<T> {
    T Value { get; }
}

[SingletonService(ServiceType = typeof(IGenericInterface<>))]
public class GenericClass<T> : IGenericInterface<T> {
    public GenericClass(T value) {
        Value = value;
    }

    public T Value {
        get;
    }
}