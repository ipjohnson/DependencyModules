using DependencyModules.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Specs.GeneratorTests;

/// <summary>
/// A service attribute registers the declaration it is written on. A nested class or a factory
/// method inside a service class has its own registration and does not add one for the class.
/// </summary>
public partial class NestedServiceAttributeTests
{
    [VerifyTest]
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

        Verify(cache.Lifetime == ServiceLifetime.Singleton);
        Verify(assembly.Descriptor("IEntry").Lifetime == ServiceLifetime.Transient);

        var provider = assembly.BuildProvider();

        Verify(
            ReferenceEquals(
                provider.GetRequiredService(assembly.Type("ICache")),
                provider.GetRequiredService(assembly.Type("ICache"))
            )
        );
    }

    [VerifyTest]
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

        Verify(assembly.Descriptor("IStore").Lifetime == ServiceLifetime.Singleton);
        Verify(assembly.Descriptor("IClock").Lifetime == ServiceLifetime.Scoped);
    }
}
