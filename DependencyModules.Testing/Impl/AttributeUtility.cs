using System.Reflection;

namespace DependencyModules.Testing.Impl;

public static class AttributeUtility {
    /// <summary>
    ///     Get attribute on a method, looks on method, then class, then assembly
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="methodInfo"></param>
    /// <returns></returns>
    public static T? GetTestAttribute<T>(this MethodInfo methodInfo) where T : class {
        var returnAttribute = methodInfo.GetCustomAttributes().FirstOrDefault(a => a is T) ??
                              methodInfo.DeclaringType?.GetTypeInfo().GetCustomAttributes()
                                  .FirstOrDefault(a => a is T) ??
                              methodInfo.DeclaringType?.GetTypeInfo().Assembly.GetCustomAttributes()
                                  .FirstOrDefault(a => a is T);

        return returnAttribute as T;
    }


    /// <summary>
    ///     Get attribute on a method, looks on method, then class, then assembly
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="parameterInfo"></param>
    /// <returns></returns>
    public static T? GetTestAttribute<T>(this ParameterInfo parameterInfo) where T : class {
        var attribute = parameterInfo.GetCustomAttributes().FirstOrDefault(a => a is T);

        if (attribute != null) {
            return attribute as T;
        }

        var methodInfo = parameterInfo.Member;

        var returnAttribute = methodInfo.GetCustomAttributes().FirstOrDefault(a => a is T) ??
                              methodInfo.DeclaringType?.GetTypeInfo().GetCustomAttributes()
                                  .FirstOrDefault(a => a is T) ??
                              methodInfo.DeclaringType?.GetTypeInfo().Assembly.GetCustomAttributes()
                                  .FirstOrDefault(a => a is T);

        return returnAttribute as T;
    }

    /// <summary>
    ///     Gets attributes from method, class, then assembly
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="methodInfo"></param>
    /// <returns></returns>
    public static IEnumerable<T> GetTestAttributes<T>(this MethodInfo methodInfo) where T : class {
        var returnList = new List<T>();

        if (methodInfo.DeclaringType != null) {
            returnList.AddRange(methodInfo.DeclaringType.GetTypeInfo().Assembly.GetCustomAttributes().OfType<T>());

            returnList.AddRange(methodInfo.DeclaringType.GetTypeInfo().GetCustomAttributes().OfType<T>());
        }

        returnList.AddRange(methodInfo.GetCustomAttributes().OfType<T>());

        return returnList;
    }

    /// <summary>
    ///     Gets attributes from method, class, then assembly
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="parameterInfo"></param>
    /// <returns></returns>
    public static IEnumerable<T> GetTestAttributes<T>(this ParameterInfo parameterInfo) where T : class {
        var returnList = new List<T>();

        var methodInfo = parameterInfo.Member;

        if (methodInfo.DeclaringType != null) {
            returnList.AddRange(methodInfo.DeclaringType.GetTypeInfo().Assembly.GetCustomAttributes().OfType<T>());

            returnList.AddRange(methodInfo.DeclaringType.GetTypeInfo().GetCustomAttributes().OfType<T>());
        }

        returnList.AddRange(methodInfo.GetCustomAttributes().OfType<T>());

        returnList.AddRange(parameterInfo.GetCustomAttributes().OfType<T>());

        return returnList;
    }
}