namespace DependencyModules.xUnit.Attributes.Interfaces;

/// <summary>
/// Represents an interface for defining a specific order of execution or processing
/// for objects or components. This interface is intended to be implemented in scenarios
/// where ordering or prioritization is necessary.
/// </summary>
/// <remarks>
/// Implementers of this interface use the 'Order' property to explicitly define
/// their precedence. This can be used in frameworks, test cases, or other
/// systems requiring deterministic or prioritized execution flows.
/// </remarks>
public interface IOrderedAttribute {
    /// <summary>
    /// Represents the execution or processing order assigned to an object or component.
    /// This property is used to specify the precedence or priority of the object
    /// implementing the interface, with lower values indicating higher priority.
    /// </summary>
    /// <remarks>
    /// The default value of the property is 10 if not explicitly overridden by the implementer.
    /// </remarks>
    int Order => 10;
}