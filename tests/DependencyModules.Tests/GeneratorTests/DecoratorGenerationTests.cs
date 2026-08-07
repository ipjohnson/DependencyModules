using DependencyModules.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// Decoration verified by resolving from a real container built from generated code, rather than by
/// matching the generated text. The registrations are the point; their shape is not.
/// </summary>
public class DecoratorGenerationTests {

    [Fact]
    public void Decorator_WrapsTheRegisteredImplementation() {
        var generated = GeneratedAssembly.Create(Module(
            """
            [Decorator]
            public class LoudGreeter(IGreeter inner) : IGreeter {
                public string Greet() => inner.Greet().ToUpperInvariant();
            }
            """));

        Assert.Equal("HELLO", Greet(generated));
    }

    [Fact]
    public void Decorator_PreservesTheServiceLifetime() {
        var generated = GeneratedAssembly.Create(Module(
            """
            [Decorator]
            public class LoudGreeter(IGreeter inner) : IGreeter {
                public string Greet() => inner.Greet();
            }
            """));

        Assert.Equal(ServiceLifetime.Singleton, generated.Descriptor("IGreeter").Lifetime);
    }

    /// <summary>
    /// Lower order sits closer to the implementation, so the higher-order decorator is outermost.
    /// </summary>
    [Fact]
    public void Decorators_NestByAscendingOrder() {
        var generated = GeneratedAssembly.Create(Module(
            """
            [Decorator(Order = 10)]
            public class InnerGreeter(IGreeter inner) : IGreeter {
                public string Greet() => $"inner({inner.Greet()})";
            }

            [Decorator(Order = 20)]
            public class OuterGreeter(IGreeter inner) : IGreeter {
                public string Greet() => $"outer({inner.Greet()})";
            }
            """));

        Assert.Equal("outer(inner(hello))", Greet(generated));
    }

    [Fact]
    public void Decorator_ReceivesItsOwnDependencies() {
        var generated = GeneratedAssembly.Create(Module(
            """
            public interface IPrefix { string Value { get; } }

            [SingletonService]
            public class Prefix : IPrefix { public string Value => "pre"; }

            [Decorator]
            public class PrefixedGreeter(IGreeter inner, IPrefix prefix) : IGreeter {
                public string Greet() => $"{prefix.Value}-{inner.Greet()}";
            }
            """));

        Assert.Equal("pre-hello", Greet(generated));
    }

    [Fact]
    public void OpenGenericDecorator_WrapsEveryClosedRegistration() {
        var generated = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IHandler<T> { string Handle(); }

            [SingletonService]
            public class StringHandler : IHandler<string> { public string Handle() => "string"; }

            [SingletonService]
            public class IntHandler : IHandler<int> { public string Handle() => "int"; }

            [Decorator]
            public class ValidatingHandler<T>(IHandler<T> inner) : IHandler<T> {
                public string Handle() => $"validated({inner.Handle()})";
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        var provider = generated.BuildProvider();
        var handler = generated.Type("IHandler`1");

        Assert.Equal("validated(string)",
            Invoke(provider.GetService(handler.MakeGenericType(typeof(string)))!, "Handle"));
        Assert.Equal("validated(int)",
            Invoke(provider.GetService(handler.MakeGenericType(typeof(int)))!, "Handle"));
    }

    [Fact]
    public void ModuleLevelDecorate_WrapsAServiceTheModuleDoesNotDeclare() {
        var generated = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            [SingletonService]
            public class Greeter : IGreeter { public string Greet() => "hello"; }

            public class LoudGreeter(IGreeter inner) : IGreeter {
                public string Greet() => inner.Greet().ToUpperInvariant();
            }

            [DependencyModule]
            [Decorate(typeof(IGreeter), typeof(LoudGreeter))]
            public partial class TestModule;
            """);

        Assert.Equal("HELLO", Greet(generated));
    }

    [Fact]
    public void Decorator_WithExplicitService_UsesIt() {
        var generated = GeneratedAssembly.Create(Module(
            """
            [Decorator(Service = typeof(IGreeter))]
            public class LoudGreeter(IGreeter inner) : IGreeter {
                public string Greet() => inner.Greet().ToUpperInvariant();
            }
            """));

        Assert.Equal("HELLO", Greet(generated));
    }

    [Fact]
    public void NoDecorator_LeavesTheServiceAlone() {
        var generated = GeneratedAssembly.Create(Module(""));

        Assert.Equal("hello", Greet(generated));
    }

    /// <summary>
    /// Two decorators of one service with the same order would nest in an order nobody declared.
    /// </summary>
    [Fact]
    public void DecoratorsSharingAnOrder_ReportDM0007() {
        var result = GeneratorTestHarness.Run(Module(
            """
            [Decorator(Order = 5)]
            public class FirstGreeter(IGreeter inner) : IGreeter {
                public string Greet() => inner.Greet();
            }

            [Decorator(Order = 5)]
            public class SecondGreeter(IGreeter inner) : IGreeter {
                public string Greet() => inner.Greet();
            }
            """));

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0007");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("IGreeter", diagnostic.GetMessage());
    }

    [Fact]
    public void DecoratorsWithDistinctOrders_ReportNothing() {
        var result = GeneratorTestHarness.Run(Module(
            """
            [Decorator(Order = 1)]
            public class FirstGreeter(IGreeter inner) : IGreeter {
                public string Greet() => inner.Greet();
            }

            [Decorator(Order = 2)]
            public class SecondGreeter(IGreeter inner) : IGreeter {
                public string Greet() => inner.Greet();
            }
            """));

        result.AssertNoErrors();
        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == "DM0007");
    }

    private static string Greet(GeneratedAssembly generated) =>
        Invoke(generated.ResolveRequired("IGreeter"), "Greet");

    private static string Invoke(object target, string method) =>
        (string)target.GetType().GetMethod(method)!.Invoke(target, null)!;

    private static string Module(string body) =>
        $$"""
          using DependencyModules.Runtime.Attributes;

          namespace TestNamespace;

          public interface IGreeter { string Greet(); }

          [SingletonService]
          public class Greeter : IGreeter { public string Greet() => "hello"; }

          {{body}}

          [DependencyModule]
          public partial class TestModule;
          """;
}
