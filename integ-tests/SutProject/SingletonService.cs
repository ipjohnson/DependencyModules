using DependencyModules.Runtime.Attributes;

namespace SutProject;

public interface ISingletonService {
    string GetName();
}

[SingletonService]
public class SingletonService : ISingletonService {

    public string GetName() {
        return nameof(SingletonService);
    }
}