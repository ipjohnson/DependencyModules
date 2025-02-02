namespace DependencyModules.xUnit.Attributes.Interfaces;

public interface IMockSupportAttribute {
    object ProvideMock(Type type);
}