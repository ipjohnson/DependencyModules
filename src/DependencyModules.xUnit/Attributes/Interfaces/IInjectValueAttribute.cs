using System.Reflection;

namespace DependencyModules.xUnit.Attributes.Interfaces;

/// <summary>
/// Attribute interface that can be used when resolving concrete types under test
/// This attribute is only applicable when the type is not registered with DI
/// rather it's instantiated using ActivatorUtilities.CreateInstance
/// </summary>
public interface IInjectValueAttribute {
    /// <summary>
    /// Provides predefined values to method parameters during runtime. This method
    /// is part of the dependency injection process and retrieves values based on
    /// the specified service provider and parameter information.
    /// </summary>
    /// <param name="serviceProvider">
    /// An instance of <see cref="IServiceProvider"/> used to resolve services during
    /// the injection process.
    /// </param>
    /// <param name="parameter">
    /// A <see cref="ParameterInfo"/> object containing metadata about the target
    /// parameter, such as its name, type, and attributes.
    /// </param>
    /// <returns>
    /// An array of objects that represent the values to be injected into the
    /// respective method parameters.
    /// </returns>
    object[] ProvideValue(IServiceProvider serviceProvider, ParameterInfo parameter);
}