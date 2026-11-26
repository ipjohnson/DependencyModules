namespace DependencyModules.Runtime.Attributes;

/// <summary>
/// Tells the generated code to build a constructor parameter with <c>new</c>, and not to get it
/// from the service provider or to give it a caller argument of a <c>Func</c>.
/// </summary>
/// <remarks>
/// The attribute has an effect only on a constructor parameter of an object that the generated
/// code builds: a class with an <c>Into*</c> attribute, the result of a <c>Func</c>, and the
/// objects that these get with <c>new</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
public sealed class FromConstructorAttribute : Attribute;
