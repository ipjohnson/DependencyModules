using System.Collections.Immutable;
using System.Reflection;

namespace DependencyModules.xUnit.Impl;

public static class AttributeUtility {
    /// <summary>
    ///     Get attribute on a method, looks on method, then class, then assembly
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="methodInfo"></param>
    /// <returns></returns>
    public static T? GetTestAttribute<T>(this MethodInfo methodInfo) where T : class {
        var returnAttribute = methodInfo.GetOrderedCustomAttributes().FirstOrDefault(a => a is T) ??
                              methodInfo.DeclaringType?.GetTypeInfo().GetOrderedCustomAttributes()
                                  .FirstOrDefault(a => a is T) ??
                              methodInfo.DeclaringType?.GetTypeInfo().Assembly.GetOrderedCustomAttributes()
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
    ///     Gets attributes from method, class, then assembly
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="methodInfo"></param>
    /// <returns></returns>
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
    ///     Gets attributes from method, class, then assembly
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="parameterInfo"></param>
    /// <returns></returns>
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
    
    private class SortCustomAttribute : IComparer<Attribute> {
        private Assembly _testAssembly;

        public SortCustomAttribute(Assembly testAssembly) {
            _testAssembly = testAssembly;
        }

        public int Compare(Attribute? x, Attribute? y) {
            if (_testAssembly.Equals(x?.GetType().Assembly)) {
                if (_testAssembly.Equals(y?.GetType().Assembly)) {
                    return 0;
                }
                return 1;
            }
            else if (_testAssembly.Equals(y?.GetType().Assembly)) {
                return -1;
            }
            
            return 0;
        }
    }
}