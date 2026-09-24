using DependencyModules.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// A service attribute registers the declaration it is written on. A nested class or a factory
/// method inside a service class has its own registration and does not add one for the class.
/// </summary>
public class NestedServiceAttributeTests
{
    [Fact]
    public void ANestedServiceClass_DoesNotRegisterItsContainingClassAgain()
    {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface ICache { }

            public interface IEntry { }

            [SingletonService]
            public class Cache : ICache
            {
                [TransientService]
                public class Entry : IEntry { }
            }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        var cache = assembly.Descriptor("ICache");

        Assert.Equal(ServiceLifetime.Singleton, cache.Lifetime);
        Assert.Equal(ServiceLifetime.Transient, assembly.Descriptor("IEntry").Lifetime);

        var provider = assembly.BuildProvider();

        Assert.Same(
            provider.GetRequiredService(assembly.Type("ICache")),
            provider.GetRequiredService(assembly.Type("ICache"))
        );
    }

    [Fact]
    public void AFactoryMethodInAServiceClass_DoesNotRegisterItsContainingClassAgain()
    {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IStore { }

            public interface IClock { }

            public class Clock : IClock { }

            [SingletonService]
            public class Store : IStore
            {
                [ScopedService]
                public static IClock CreateClock() => new Clock();
            }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        Assert.Equal(ServiceLifetime.Singleton, assembly.Descriptor("IStore").Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, assembly.Descriptor("IClock").Lifetime);
    }
}
