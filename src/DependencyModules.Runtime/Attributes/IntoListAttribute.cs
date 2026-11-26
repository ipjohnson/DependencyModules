namespace DependencyModules.Runtime.Attributes;

/// <summary>
/// Adds the class to the list of <typeparamref name="T"/>, with an order that comes from types.
/// A class gets the list as <c>IReadOnlyList&lt;T&gt;</c>.
/// </summary>
/// <remarks>
/// The class is not registered as a service. The list builds an item when it is first read, and
/// gets the constructor parameters from the service provider.
/// </remarks>
/// <typeparam name="T">The type of the items of the list.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class IntoListAttribute<T> : Attribute
{
    /// <param name="dependsOn">
    /// The class comes after each item of the list that is assignable to one of these types. A
    /// type with no item in the list has no effect.
    /// </param>
    public IntoListAttribute(params Type[] dependsOn)
    {
        DependsOn = dependsOn;
    }

    /// <summary>
    /// The types that the class comes after.
    /// </summary>
    public Type[] DependsOn { get; }

    /// <summary>
    /// The module that adds the class to the list. If it is not set, each module of the project
    /// that is not realm-only adds the class.
    /// </summary>
    public Type? Realm { get; set; }
}

/// <summary>
/// Adds the class to the list of <typeparamref name="T"/>, with an order that comes from values of
/// <typeparamref name="TOrder"/>. A class gets the list as <c>IReadOnlyList&lt;T&gt;</c>.
/// </summary>
/// <remarks>
/// The class is not registered as a service. The list builds an item when it is first read, and
/// gets the constructor parameters from the service provider.
/// </remarks>
/// <typeparam name="T">The type of the items of the list.</typeparam>
/// <typeparam name="TOrder">The type of the order values, for example a number or an enum.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class IntoListAttribute<T, TOrder> : Attribute
{
    /// <param name="value">
    /// The order value of the class. More than one item can have the same value. The list sorts
    /// items by this value when no other order applies.
    /// </param>
    /// <param name="dependsOn">
    /// The class comes after each item of the list that has one of these values. A value with no
    /// item in the list has no effect.
    /// </param>
    public IntoListAttribute(TOrder value, params TOrder[] dependsOn)
    {
        Value = value;
        DependsOn = dependsOn;
    }

    /// <summary>
    /// The order value of the class.
    /// </summary>
    public TOrder Value { get; }

    /// <summary>
    /// The values that the class comes after.
    /// </summary>
    public TOrder[] DependsOn { get; }

    /// <summary>
    /// The module that adds the class to the list. If it is not set, each module of the project
    /// that is not realm-only adds the class.
    /// </summary>
    public Type? Realm { get; set; }
}
