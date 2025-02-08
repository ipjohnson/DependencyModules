using DependencyModules.Runtime.Attributes;

namespace SutProject.Tests.KeyedTests;

public interface IKeyedRegistration {
    string Key { get; }
}

[SingletonService(Key = "A", Realm = typeof(KeyedModule))]
public class AKeyedRegistration : KeyedRegistration {
    public AKeyedRegistration() : base("A") { }
}

[SingletonService(Key = "B")]
public class BKeyedRegistration : KeyedRegistration {
    public BKeyedRegistration() : base("B") {
    }
}

[SingletonService(Key = "C")]
public class CKeyedRegistration : KeyedRegistration {
    public CKeyedRegistration() : base("C") {
    }
}

public abstract class KeyedRegistration : IKeyedRegistration {
    protected KeyedRegistration(string key) {
        Key = key;
    }

    public string Key { get; }
}