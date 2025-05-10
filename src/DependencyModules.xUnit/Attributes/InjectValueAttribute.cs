using System.Reflection;
using DependencyModules.xUnit.Attributes.Interfaces;

namespace DependencyModules.xUnit.Attributes;

public class InjectValueAttribute(object value) : Attribute, IInjectValueAttribute {

    public object ProvideValue(IServiceProvider serviceProvider, ParameterInfo parameter) {
        return value;
    }
}