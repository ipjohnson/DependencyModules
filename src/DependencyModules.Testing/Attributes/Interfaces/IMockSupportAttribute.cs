namespace DependencyModules.Testing.Attributes.Interfaces;

public interface IMockSupportAttribute {
    object ProvideMock(Type type);
}