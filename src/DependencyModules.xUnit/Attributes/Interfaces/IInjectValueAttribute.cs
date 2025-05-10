using System.Reflection;

namespace DependencyModules.xUnit.Attributes.Interfaces;

/// <summary>
/// Attribute interface that can be used when resolving concrete types under test
/// This attribute is only applicable when the type is not registered with DI
/// rather it's instantiated using ActivatorUtilities.CreateInstance
/// </summary>
public interface IInjectValueAttribute {
    object? ProvideValue(IServiceProvider serviceProvider, ParameterInfo parameter);
}