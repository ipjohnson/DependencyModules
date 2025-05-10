using System.Collections.Immutable;
using System.Reflection;

namespace DependencyModules.xUnit.Impl;

/// <summary>
/// Provides utility methods for retrieving attributes from methods, parameters, classes, or assemblies.
/// </summary>
public static class AttributeUtility {

    /// <summary>
    /// Retrieves an attribute of the specified type from a method.
    /// Searches on the method, then its declaring type, and finally its assembly.
    /// </summary>
    /// <typeparam name="T">The type of the attribute to retrieve.</typeparam>
    /// <param name="methodInfo">The method from which to retrieve the attribute.</param>
    /// <returns>The first matching attribute of the specified type, or null if no attribute is found.</returns>
    public static T? GetTestAttribute<T>(this MethodInfo methodInfo) where T : class {
        var returnAttribute = methodInfo.GetOrderedCustomAttributes().FirstOrDefault(a => a is T) ??
                              methodInfo.DeclaringType?.GetTypeInfo().GetOrderedCustomAttributes()
                                  .FirstOrDefault(a => a is T) ??
                              methodInfo.DeclaringType?.GetTypeInfo().Assembly.GetOrderedCustomAttributes()
                                  .FirstOrDefault(a => a is T);

        return returnAttribute as T;
    }

    /// <summary>
    /// Retrieves an attribute of the specified type from a method parameter.
    /// Searches on the parameter, the containing method, the declaring type, and finally the assembly.
    /// </summary>
    /// <typeparam name="T">The type of the attribute to retrieve.</typeparam>
    /// <param name="parameterInfo">The parameter from which to retrieve the attribute.</param>
    /// <returns>The first matching attribute of the specified type, or null if no attribute is found.</returns>
    public static T? GetTestAttribute<T>(this ParameterInfo parameterInfo) where T : class {
        var attribute = parameterInfo.GetOrderedCustomAttributes().FirstOrDefault(a => a is T);

        if (attribute != null) {
            return attribute as T;
        }

        var methodInfo = parameterInfo.Member;

        var returnAttribute = methodInfo.GetOrderedCustomAttributes().FirstOrDefault(a => a is T) ??
                              methodInfo.DeclaringType?.GetTypeInfo().GetOrderedCustomAttributes()
                                  .FirstOrDefault(a => a is T) ??
                              methodInfo.DeclaringType?.GetTypeInfo().Assembly.GetOrderedCustomAttributes()
                                  .FirstOrDefault(a => a is T);

        return returnAttribute as T;
    }


    /// <summary>
    /// Retrieves all attributes of the specified type from a method.
    /// Searches the method, its declaring type, and its assembly in order to accumulate matching attributes.
    /// </summary>
    /// <typeparam name="T">The type of the attributes to retrieve.</typeparam>
    /// <param name="methodInfo">The method from which to retrieve the attributes.</param>
    /// <returns>An enumerable collection of attributes of the specified type.</returns>
    public static IEnumerable<T> GetTestAttributes<T>(this MethodInfo methodInfo) where T : class {
        var returnList = new List<T>();

        if (methodInfo.DeclaringType != null) {
            returnList.AddRange(methodInfo.DeclaringType.GetTypeInfo().Assembly.GetOrderedCustomAttributes().OfType<T>());

            returnList.AddRange(methodInfo.DeclaringType.GetTypeInfo().GetOrderedCustomAttributes().OfType<T>());
        }

        returnList.AddRange(methodInfo.GetOrderedCustomAttributes().OfType<T>());

        return returnList;
    }


    /// <summary>
    /// Retrieves all attributes of the specified type from a parameterInfo.
    /// Searches on the method, its declaring type, and its assembly in order.
    /// </summary>
    /// <typeparam name="T">The type of the attributes to retrieve.</typeparam>
    /// <param name="parameterInfo">The parameter from which to retrieve the attributes.</param>
    /// <returns>A collection of matching attributes of the specified type, or an empty collection if no attributes are found.</returns>
    public static IEnumerable<T> GetTestAttributes<T>(this ParameterInfo parameterInfo) where T : class {
        var returnList = new List<T>();

        var methodInfo = parameterInfo.Member;

        if (methodInfo.DeclaringType != null) {
            returnList.AddRange(methodInfo.DeclaringType.GetTypeInfo().Assembly.GetOrderedCustomAttributes().OfType<T>());

            returnList.AddRange(methodInfo.DeclaringType.GetTypeInfo().GetOrderedCustomAttributes().OfType<T>());
        }

        returnList.AddRange(methodInfo.GetOrderedCustomAttributes().OfType<T>());

        returnList.AddRange(parameterInfo.GetOrderedCustomAttributes().OfType<T>());

        return returnList;
    }

    private static IEnumerable<Attribute> GetOrderedCustomAttributes(this Assembly assembly) {
        return assembly.GetCustomAttributes()
            .Order(new SortCustomAttribute(assembly));
    }
    
    private static IEnumerable<Attribute> GetOrderedCustomAttributes(this Type type) {
        return type.GetCustomAttributes()
            .Order(new SortCustomAttribute(type.Assembly));
    }
    
    private static IEnumerable<Attribute> GetOrderedCustomAttributes(this MemberInfo? memberInfo) {
        if (memberInfo?.DeclaringType == null) {
            return ArraySegment<Attribute>.Empty;
        }

        return memberInfo.GetCustomAttributes()
            .Order(new SortCustomAttribute(memberInfo.DeclaringType.Assembly));
    }
    
    private static IEnumerable<Attribute> GetOrderedCustomAttributes(this ParameterInfo? parameterInfo) {
        if (parameterInfo?.Member.DeclaringType == null) {
            return ArraySegment<Attribute>.Empty;
        }

        return parameterInfo.GetCustomAttributes()
            .Order(new SortCustomAttribute(parameterInfo.Member.DeclaringType.Assembly));
    }

    /// <summary>
    /// Provides a custom attribute sorting mechanism based on the association of attributes with a specified assembly.
    /// </summary>
    private class SortCustomAttribute(Assembly testAssembly) : IComparer<Attribute> {

        public int Compare(Attribute? x, Attribute? y) {
            if (testAssembly.Equals(x?.GetType().Assembly)) {
                if (testAssembly.Equals(y?.GetType().Assembly)) {
                    return 0;
                }
                return 1;
            }
            else if (testAssembly.Equals(y?.GetType().Assembly)) {
                return -1;
            }
            
            return 0;
        }
    }
}