namespace DependencyModules.Runtime.Attributes;

/// <summary>
/// Tells the generated code to get a constructor parameter from the service provider, and not to
/// build it with <c>new</c> or to give it a caller argument of a <c>Func</c>.
/// </summary>
/// <remarks>
/// The attribute has an effect only on a constructor parameter of an object that the generated
/// code builds: a class with an <c>Into*</c> attribute, the result of a <c>Func</c>, and the
/// objects that these get with <c>new</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
public sealed class FromServiceProviderAttribute : Attribute;
