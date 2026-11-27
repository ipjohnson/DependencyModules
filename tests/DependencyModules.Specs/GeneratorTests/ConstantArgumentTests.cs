using DependencyModules.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Specs.GeneratorTests;

/// <summary>
/// An attribute or convention argument is read as the value it evaluates to. A constant declared
/// elsewhere, a digit separator and a fully qualified enum member all have to give the same result
/// as the literal.
/// </summary>
public partial class ConstantArgumentTests
{
    private const string Preamble = """
        using DependencyModules.Runtime.Attributes;
        using DependencyModules.Runtime.Conventions;
        using DependencyModules.Runtime.Interception;
        using Microsoft.Extensions.DependencyInjection;

        namespace TestNamespace;

        public static class Orders
        {
            public const int Late = 2000;
        }

        public static class Choices
        {
            public const RegistrationType TryIt = RegistrationType.Try;
            public const InterceptedMembers MethodsOnly = InterceptedMembers.Methods;
            public const ServiceLifetime PerScope = ServiceLifetime.Scoped;
            public const bool Yes = true;
        }

        """;

    private static GeneratorResult Run(string source) =>
        GeneratorTestHarness.Run(Preamble + source);

    private static GeneratedAssembly Compile(string source) =>
        GeneratedAssembly.Create(Preamble + source);

    [VerifyTest]
    public void ServiceOrder_IsReadAsItsValue()
    {
        var assembly = Compile(
            """
            public interface IRule { }

            [SingletonService(Order = Orders.Late)]
            public class ConstantOrderRule : IRule { }

            [SingletonService(Order = 1_000)]
            public class SeparatorOrderRule : IRule { }

            [SingletonService(Order = 5)]
            public class LiteralOrderRule : IRule { }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        Verify(
            assembly
                .Descriptors("IRule")
                .Select(d => d.ImplementationType!.Name)
                .SequenceEqual(
                    new[] { "LiteralOrderRule", "SeparatorOrderRule", "ConstantOrderRule" }
                )
        );
    }

    [VerifyTest]
    [Row("DependencyModules.Runtime.Attributes.RegistrationType.Try")]
    [Row("Choices.TryIt")]
    public void ServiceUsing_IsReadAsItsValue(string registrationType)
    {
        var assembly = Compile(
            $$"""
            public interface IClock { }

            [SingletonService(Using = {{registrationType}})]
            public class FirstClock : IClock { }

            [SingletonService(Using = {{registrationType}})]
            public class SecondClock : IClock { }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        Single(assembly.Descriptors("IClock"));
    }

    [VerifyTest]
    public void ModuleArguments_AreReadAsTheirValues()
    {
        var result = Run(
            """
            public interface IClock { }

            [SingletonService]
            public class FirstClock : IClock { }

            [SingletonService]
            public class SecondClock : IClock { }

            [DependencyModule(Using = Choices.TryIt, GenerateFactories = Choices.Yes)]
            public partial class TestModule;
            """
        );

        result.AssertNoErrors();

        var registrations = result.SourceContaining("Dependencies");

        Verify(registrations.Contains("services.TryAddSingleton("));
        Verify(!registrations.Contains("services.AddSingleton("));
        Verify(registrations.Contains("new global::TestNamespace.FirstClock()"));
    }

    [VerifyTest]
    public void CrossWireLifetime_IsReadAsItsValue()
    {
        var assembly = Compile(
            """
            public interface IReader { }

            [CrossWireService(Lifetime = Choices.PerScope)]
            public class Store : IReader { }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        Verify(assembly.Descriptor("Store").Lifetime == ServiceLifetime.Scoped);
        Verify(assembly.Descriptor("IReader").Lifetime == ServiceLifetime.Scoped);
    }

    [VerifyTest]
    public void DecoratorOrder_IsReadAsItsValue()
    {
        var assembly = Compile(
            """
            public interface IGreeter
            {
                string Greet();
            }

            [SingletonService]
            public class Greeter : IGreeter
            {
                public string Greet() => "hello";
            }

            [Decorator(Order = Orders.Late)]
            public class OuterGreeter(IGreeter inner) : IGreeter
            {
                public string Greet() => $"outer({inner.Greet()})";
            }

            [Decorator(Order = 1_000)]
            public class InnerGreeter(IGreeter inner) : IGreeter
            {
                public string Greet() => $"inner({inner.Greet()})";
            }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        var greeter = assembly.ResolveRequired("IGreeter");

        Verify(
            Equals(
                greeter.GetType().GetMethod("Greet")!.Invoke(greeter, null),
                "outer(inner(hello))"
            )
        );
    }

    [VerifyTest]
    public void InterceptArguments_AreReadAsTheirValues()
    {
        var assembly = Compile(
            """
            public interface IGreeter
            {
                string Hello();

                string Name { get; }
            }

            public class CountingInterceptor : IInterceptor
            {
                public static int Calls;

                public TResult Intercept<TResult>(InvocationContext<TResult> context)
                {
                    Calls++;
                    return context.Proceed();
                }
            }

            [SingletonService]
            [Intercept(
                typeof(CountingInterceptor),
                Order = Orders.Late,
                Members = Choices.MethodsOnly,
                Lifetime = Choices.PerScope
            )]
            public class Greeter : IGreeter
            {
                public string Hello() => "hello";

                public string Name => "greeter";
            }

            [Decorator(Order = 1_000)]
            public class LoggingGreeter(IGreeter inner) : IGreeter
            {
                public string Hello() => inner.Hello();

                public string Name => inner.Name;
            }

            [DependencyModule]
            public partial class TestModule;
            """
        );

        Verify(assembly.Descriptor("CountingInterceptor").Lifetime == ServiceLifetime.Scoped);

        using var scope = assembly.BuildProvider().CreateScope();
        var greeterType = assembly.Type("IGreeter");
        var greeter = scope.ServiceProvider.GetRequiredService(greeterType);

        // Order 2000 puts the interception outside the decorator at 1000.
        Verify(greeter.GetType().Name == "Greeter_Intercepted");

        greeterType.GetMethod("Hello")!.Invoke(greeter, null);
        greeterType.GetProperty("Name")!.GetValue(greeter);

        Verify(Equals(assembly.Type("CountingInterceptor").GetField("Calls")!.GetValue(null), 1));
    }

    [VerifyTest]
    public void ConventionUsing_IsReadAsItsValue()
    {
        var result = Run(
            """
            public interface IHandler { }

            public class OrderHandler : IHandler { }

            [DependencyModule]
            public partial class TestModule : IConventionModule
            {
                public void Conventions(IConventionDefinitions conventions)
                {
                    conventions.RegisterAll<IHandler>().Using(Choices.TryIt).AsSingleton();
                }
            }
            """
        );

        result.AssertNoErrors();

        Verify(
            result.SourceContaining("ConventionDependencies").Contains("services.TryAddSingleton(")
        );
    }

    [VerifyTest]
    [Row("IfEnvironmentValue(Keys.Feature, \"on\")", "'Keys.Feature'")]
    [Row("WithName(\"Order*\", Keys.InvoicePattern)", "'Keys.InvoicePattern'")]
    [Row("Using(Keys.Using)", "RegistrationType")]
    public void AConventionArgumentThatIsNotAConstant_IsReported(string call, string named)
    {
        var result = Run(
            $$"""
            public static class Keys
            {
                public static readonly string Feature = "FEATURE_X";
                public static readonly string InvoicePattern = "Invoice*";
                public static readonly RegistrationType Using = RegistrationType.Try;
            }

            public interface IHandler { }

            public class OrderHandler : IHandler { }

            [DependencyModule]
            public partial class TestModule : IConventionModule
            {
                public void Conventions(IConventionDefinitions conventions)
                {
                    conventions.RegisterAll<IHandler>().{{call}}.AsSingleton();
                }
            }
            """
        );

        var diagnostic = Single(result.GeneratorDiagnostics, d => d.Id == "DM0009");
        Verify(diagnostic.GetMessage().Contains(named));
    }

    [VerifyTest]
    public void AConstantConventionArgument_IsRead()
    {
        var result = Run(
            """
            public static class Keys
            {
                public const string Feature = "FEATURE_X";
            }

            public interface IHandler { }

            public class OrderHandler : IHandler { }

            [DependencyModule]
            public partial class TestModule : IConventionModule
            {
                public void Conventions(IConventionDefinitions conventions)
                {
                    conventions.RegisterAll<IHandler>().IfEnvironmentValue(Keys.Feature, "on").AsSingleton();
                }
            }
            """
        );

        result.AssertNoErrors();

        Verify(!result.GeneratorDiagnostics.Any(d => d.Id == "DM0009"));
        Verify(result.SourceContaining("ConventionDependencies").Contains("\"FEATURE_X\""));
    }
}
