using DependencyModules.xUnit.Attributes.Interfaces;
using NSub = NSubstitute;

namespace DependencyModules.xUnit.NSubstitute;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Assembly)]
public class NSubstituteSupportAttribute : Attribute, IMockSupportAttribute {

    public object ProvideMock(Type type) {
        return NSub.Substitute.For([type], []);
    }
}