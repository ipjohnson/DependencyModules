using DependencyModules.Runtime.Attributes;

namespace SutProject;

public interface IScopedService { }

[ScopedService]
public class ScopedService : IScopedService { }