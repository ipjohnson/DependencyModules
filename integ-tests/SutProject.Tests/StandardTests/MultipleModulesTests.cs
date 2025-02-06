using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using DependencyModules.xUnit.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SutProject.Tests.StandardTests;

public class StringWrapper(string value) {
    public string Value { get; } = value;
}

[DependencyModule]
public partial class UniqueModule : IServiceCollectionConfiguration{
    private readonly string _value;

    public UniqueModule(string value) {
        _value = value;
    }
    
    public override bool Equals(object? obj) {
        if (obj is UniqueModule other) {
            return _value == other._value;
        }
        return false;
    }
    
    public override int GetHashCode() {
        return _value.GetHashCode();
    }

    public void ConfigureServices(IServiceCollection services) {
        services.AddTransient(_ => new StringWrapper(_value));
    }
}

public class MultipleModulesTests {
    [ModuleTest]
    [UniqueModule.Attribute("test-value")]
    [UniqueModule.Attribute("test-value-2")]
    public void MultipleModuleTest(IEnumerable<StringWrapper> wrappers) {
        var wrapperList = wrappers.ToList();
        Assert.Equal(2, wrapperList.Count);
        Assert.Contains(wrapperList, wrapper => wrapper.Value == "test-value");
        Assert.Contains(wrapperList, wrapper => wrapper.Value == "test-value-2");
    }
    
    [ModuleTest]
    [UniqueModule.Attribute("test-value")]
    [UniqueModule.Attribute("test-value")]
    public void SameMultipleModuleTest(IEnumerable<StringWrapper> wrappers) {
        var wrapperList = wrappers.ToList();
        Assert.Single(wrapperList);
        Assert.Contains(wrapperList, wrapper => wrapper.Value == "test-value");
    }
}