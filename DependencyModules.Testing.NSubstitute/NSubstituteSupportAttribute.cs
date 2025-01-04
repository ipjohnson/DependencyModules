using DependencyModules.Testing.Attributes.Interfaces;
using NSub = NSubstitute;

namespace DependencyModules.Testing.NSubstitute;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Assembly)]
public class NSubstituteSupportAttribute : Attribute, IMockSupportAttribute {

    public object ProvideMock(Type type) {
        return NSub.Substitute.For([type], []);
    }
}