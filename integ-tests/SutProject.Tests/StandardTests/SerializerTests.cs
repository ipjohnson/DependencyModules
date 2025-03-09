using System.Text.Json.Serialization.Metadata;
using DependencyModules.xUnit.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SutProject.Tests.StandardTests;

public class SerializerTests {
    [ModuleTest]
    [SutModule.Attribute]
    public void LoadSerializer(IEnumerable<IJsonTypeInfoResolver> resolvers) {
        var resolverList = resolvers.ToList();
        
        Assert.Single(resolverList);
        Assert.IsType<SerializerContext>(resolverList.First());
    }

    [ModuleTest]
    [SutModule.Attribute]
    [InlineData("A")]
    [InlineData("B")]
    public void LoadKeyedASerializer(string key, IServiceProvider serviceProvider) {
        var resolverList =
            serviceProvider.GetKeyedServices<IJsonTypeInfoResolver>(key).ToList();
        
        Assert.Single(resolverList);
        Assert.EndsWith(key, resolverList[0].GetType().Name);
    }
}