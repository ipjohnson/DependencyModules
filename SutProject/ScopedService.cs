using DependencyModules.Runtime.Attributes;

namespace SutProject;

public interface IScopedService {
    
}

[ScopedService(ServiceType = typeof(IScopedService))]
public class ScopedService : IScopedService {
    
}