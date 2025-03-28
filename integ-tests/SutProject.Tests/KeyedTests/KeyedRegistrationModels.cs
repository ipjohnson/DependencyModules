using DependencyModules.Runtime.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace SutProject.Tests.KeyedTests;

public interface IKeyedRegistration {
    string Key { get; }
}

[SingletonService(Key = "A", Realm = typeof(KeyedModule))]
public class AKeyedRegistration : KeyedRegistration {
    public AKeyedRegistration() : base("A") { }
}

[SingletonService(Key = "B", Realm = typeof(KeyedModule))]
public class BKeyedRegistration : KeyedRegistration {
    public BKeyedRegistration() : base("B") {
    }
}

[SingletonService(Key = "C", Realm = typeof(KeyedModule))]
public class CKeyedRegistration : KeyedRegistration {
    public CKeyedRegistration() : base("C") {
    }
}

[SingletonService]
public class CKeyedDependency([FromKeyedServices("C")] IKeyedRegistration registration) {
    public IKeyedRegistration Registration {
        get;
    } = registration;

}

public abstract class KeyedRegistration : IKeyedRegistration {
    protected KeyedRegistration(string key) {
        Key = key;
    }

    public string Key { get; }
}