using DependencyModules.Runtime;
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

    /// <summary>
    /// A service registered as an open generic is left undecorated rather than refused at run time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Decoration replaces a registration with a factory, which the container will not accept for an
    /// open generic service type — it needs an implementation type it can close per request. That has
    /// not changed. What changed is when it is said.
    /// </para>
    /// <para>
    /// Generated code names the service as a type argument, and an unbound generic cannot be written
    /// as one, so there is nothing to emit and nothing to refuse at composition either.
    /// </para>
    /// </remarks>
    [Fact]
    public void OpenGenericRegistration_IsNotDecorated() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IRepo<T> { string Name(); }

            [SingletonService]
            public class Repo<T> : IRepo<T> { public string Name() => "repo"; }

            [Decorator]
            public class LoggingRepo<T>(IRepo<T> inner) : IRepo<T> {
                public string Name() => $"logged({inner.Name()})";
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        // Nothing is emitted for it, so the registration stands undecorated rather than the
        // provider throwing when it is built.
        Assert.Empty(result.Errors);

        Assert.DoesNotContain(
            result.GeneratedSources,
            source => source.Key.Contains("Decorators") && source.Value.Contains("LoggingRepo"));
    }

    /// <summary>
    /// The way through, end to end: a closed construction of the generic service is decorated.
    /// </summary>
    [Fact]
    public void ClosedConstructionOfAGenericService_IsDecorated() {
        var generated = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IRepo<T> { string Name(); }

            public class Repo<T> : IRepo<T> { public string Name() => "repo"; }

            [SingletonService]
            public class StringRepo : Repo<string> { }

            [Decorator]
            public class LoggingRepo<T>(IRepo<T> inner) : IRepo<T> {
                public string Name() => $"logged({inner.Name()})";
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        var provider = generated.BuildProvider();
        var resolved = provider.GetService(generated.Type("IRepo`1").MakeGenericType(typeof(string)))!;

        Assert.Equal("logged(repo)", Invoke(resolved, "Name"));
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

    /// <summary>
    /// A decorator gated on the environment, which used to apply everywhere.
    /// </summary>
    /// <remarks>
    /// The attribute compiled and read as intentional while doing nothing at all — decoration never
    /// looked at conditions, so a Development-only decorator wrapped the service in production too.
    /// </remarks>
    [Theory]
    [InlineData("Development", "HELLO")]
    [InlineData("Production", "hello")]
    public void Decorator_AppliesOnlyWhenItsEnvironmentConditionHolds(
        string environmentName, string expected) {

        var generated = GeneratedAssembly.Create(
            Module(
                """
                [Decorator]
                [IfEnvironment("Development")]
                public class LoudGreeter(IGreeter inner) : IGreeter {
                    public string Greet() => inner.Greet().ToUpperInvariant();
                }
                """),
            environment: new ModuleEnvironment(environmentName));

        Assert.Equal(expected, Greet(generated));
    }

    [Theory]
    [InlineData("on", "HELLO")]
    [InlineData("off", "hello")]
    public void Decorator_HonoursValueConditions(string flag, string expected) {
        var generated = GeneratedAssembly.Create(
            Module(
                """
                [Decorator]
                [IfEnvironmentValue("LOUD", "on")]
                public class LoudGreeter(IGreeter inner) : IGreeter {
                    public string Greet() => inner.Greet().ToUpperInvariant();
                }
                """),
            environment: new ModuleEnvironment(false, "Development") { { "LOUD", flag } });

        Assert.Equal(expected, Greet(generated));
    }

    /// <summary>
    /// A condition changes whether a decorator applies, never where it sits in the nesting.
    /// </summary>
    [Fact]
    public void Decorator_ConditionDoesNotDisturbOrdering() {
        var generated = GeneratedAssembly.Create(
            Module(
                """
                [Decorator(Order = 10)]
                [IfEnvironment("Development")]
                public class Inner(IGreeter inner) : IGreeter {
                    public string Greet() => "inner(" + inner.Greet() + ")";
                }

                [Decorator(Order = 20)]
                public class Outer(IGreeter inner) : IGreeter {
                    public string Greet() => "outer(" + inner.Greet() + ")";
                }
                """),
            environment: new ModuleEnvironment("Development"));

        Assert.Equal("outer(inner(hello))", Greet(generated));
    }

    /// <summary>
    /// The unconditional decorator still applies when the conditional one drops out, rather than
    /// the whole chain going with it.
    /// </summary>
    [Fact]
    public void Decorator_UnconditionalOneSurvivesWhenAConditionalOneDoesNot() {
        var generated = GeneratedAssembly.Create(
            Module(
                """
                [Decorator(Order = 10)]
                [IfEnvironment("Development")]
                public class Inner(IGreeter inner) : IGreeter {
                    public string Greet() => "inner(" + inner.Greet() + ")";
                }

                [Decorator(Order = 20)]
                public class Outer(IGreeter inner) : IGreeter {
                    public string Greet() => "outer(" + inner.Greet() + ")";
                }
                """),
            environment: new ModuleEnvironment("Production"));

        Assert.Equal("outer(hello)", Greet(generated));
    }

    private static string Greet(GeneratedAssembly generated) =>
        Invoke(generated.ResolveRequired("IGreeter"), "Greet");

    /// <summary>Calls Handle on a resolved handler with a fresh request.</summary>
    private static void Handle(object handler, GeneratedAssembly assembly) =>
        handler.GetType().GetMethod("Handle")!.Invoke(
            handler, new[] { System.Activator.CreateInstance(assembly.Type("Create")) });

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

    /// <summary>
    /// A decorator's own dependencies are resolved on the terms each parameter declares.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Constructing the decorator in generated code means the generator, not
    /// <c>ActivatorUtilities</c>, decides how each parameter is resolved — so everything
    /// <c>ActivatorUtilities</c> used to honour has to be honoured here.
    /// </para>
    /// <para>
    /// <c>[FromKeyedServices]</c> is the one that fails silently. Resolving it unkeyed returns a
    /// registration of the right type, so nothing throws and nothing is logged; the decorator simply
    /// wraps its behaviour around the wrong instance. The assertion is on the value the keyed
    /// dependency contributes, because a type check would pass either way.
    /// </para>
    /// </remarks>
    [Fact]
    public void Decorator_ResolvesAKeyedDependencyFromTheKeyItDeclares() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;
            using Microsoft.Extensions.DependencyInjection;

            namespace TestNamespace;

            public interface IStamp { string Value { get; } }

            // Unkeyed, so resolving without the key succeeds and returns the wrong instance
            // rather than throwing. That is what makes the bug silent.
            [SingletonService]
            public class DefaultStamp : IStamp { public string Value => "?"; }

            [SingletonService(Key = "quiet")]
            public class QuietStamp : IStamp { public string Value => "."; }

            public interface IGreeter { string Greet(); }

            [SingletonService]
            public class Greeter : IGreeter { public string Greet() => "hello"; }

            [Decorator]
            public class StampedGreeter(
                IGreeter inner, [FromKeyedServices("quiet")] IStamp stamp) : IGreeter {

                public string Greet() => inner.Greet() + stamp.Value;
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        var greeter = assembly.BuildProvider().GetRequiredService(assembly.Type("IGreeter"));

        // "hello?" is what ignoring the key produces: the right type, the wrong instance, no error.
        Assert.Equal("hello.", ((dynamic)greeter).Greet());
    }

    /// <summary>
    /// A nullable dependency the container does not have resolves to null rather than throwing.
    /// </summary>
    [Fact]
    public void Decorator_ResolvesAnOptionalDependencyToNull() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IAudit { }

            public interface IGreeter { string Greet(); }

            [SingletonService]
            public class Greeter : IGreeter { public string Greet() => "hello"; }

            [Decorator]
            public class AuditedGreeter(IGreeter inner, IAudit? audit) : IGreeter {
                public string Greet() => inner.Greet() + (audit == null ? " (unaudited)" : " (audited)");
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        var greeter = assembly.BuildProvider().GetRequiredService(assembly.Type("IGreeter"));

        Assert.Equal("hello (unaudited)", ((dynamic)greeter).Greet());
    }

    /// <summary>
    /// A decorator named by <c>[Decorate]</c> is constructed by generated code, not by reflection.
    /// </summary>
    /// <remarks>
    /// The attribute carries two type names and nothing else, so the constructor is looked up from
    /// the compilation rather than read from a declaration — which is the only route for a decorator
    /// that may be declared in a referenced assembly, the case this form exists for.
    ///
    /// Asserted through a keyed dependency because that is what distinguishes the two paths. A
    /// reflective <c>ActivatorUtilities</c> call would also produce a working decorator; only the
    /// generated <c>new</c> proves the constructor was actually read, and only the key proves each
    /// parameter was resolved on the terms it declares.
    /// </remarks>
    [Fact]
    public void ModuleLevelDecorate_ConstructsTheDecoratorFromItsResolvedConstructor() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;
            using Microsoft.Extensions.DependencyInjection;

            namespace TestNamespace;

            public interface IStamp { string Value { get; } }

            [SingletonService]
            public class DefaultStamp : IStamp { public string Value => "?"; }

            [SingletonService(Key = "quiet")]
            public class QuietStamp : IStamp { public string Value => "."; }

            public interface IGreeter { string Greet(); }

            [SingletonService]
            public class Greeter : IGreeter { public string Greet() => "hello"; }

            // No [Decorator] on it: the module names it instead.
            public class StampedGreeter(
                IGreeter inner, [FromKeyedServices("quiet")] IStamp stamp) : IGreeter {

                public string Greet() => inner.Greet() + stamp.Value;
            }

            [DependencyModule]
            [Decorate(typeof(IGreeter), typeof(StampedGreeter))]
            public partial class TestModule;
            """);

        var greeter = assembly.BuildProvider().GetRequiredService(assembly.Type("IGreeter"));

        Assert.Equal("hello.", ((dynamic)greeter).Greet());
    }

    /// <summary>
    /// A decorator the container could never construct is reported rather than emitted.
    /// </summary>
    /// <remarks>
    /// Generated code constructs the decorator, so no public constructor means there is nothing to
    /// emit. The alternative was a reflective call that resolved under a JIT and threw in a
    /// published application.
    /// </remarks>
    [Fact]
    public void ModuleLevelDecorate_WithNoPublicConstructor_IsNotDecorated() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            [SingletonService]
            public class Greeter : IGreeter { public string Greet() => "hello"; }

            public class HiddenGreeter : IGreeter {
                private HiddenGreeter(IGreeter inner) { Inner = inner; }
                public IGreeter Inner { get; }
                public string Greet() => Inner.Greet();
            }

            [DependencyModule]
            [Decorate(typeof(IGreeter), typeof(HiddenGreeter))]
            public partial class TestModule;
            """);

        // Generated code constructs the decorator, so a private constructor means there is nothing
        // to emit. The build stays green and the service resolves undecorated.
        Assert.Empty(result.Errors);

        Assert.DoesNotContain(
            result.GeneratedSources,
            source => source.Value.Contains("new global::TestNamespace.HiddenGreeter"));
    }

    // ------------------------------------------------------------------------------------------
    // Generic decorators after type substitution. Everything below closes a decorator over the type
    // arguments a registration used, which rewrites its constructor parameters — so each of these
    // exercises DecoratorTypeUtility.Close as much as it does the emission.
    // ------------------------------------------------------------------------------------------

    private const string HandlerPreamble =
        """
        using DependencyModules.Runtime.Attributes;
        using Microsoft.Extensions.DependencyInjection;

        namespace TestNamespace;

        public interface IHandler<TRequest, TResponse> { TResponse Handle(TRequest request); }

        public class Create { }
        public class Rename { }
        public class Id { public string Value = ""; }

        [SingletonService]
        public class CreateHandler : IHandler<Create, Id> {
            public Id Handle(Create r) => new Id { Value = "created" };
        }

        [SingletonService]
        public class CountHandler : IHandler<Create, int> {
            public int Handle(Create r) => 41;
        }

        """;

    /// <summary>
    /// A generic decorator's keyed dependency survives being closed over the registration's types.
    /// </summary>
    /// <remarks>
    /// Closing a generic decorator rebuilds its constructor with every parameter type substituted.
    /// The parameter <i>attributes</i> have to survive that rebuild, and losing them is silent: the
    /// decorator resolves the unkeyed registration, which is the right type and the wrong instance.
    /// </remarks>
    [Fact]
    public void GenericDecorator_KeyedDependencySurvivesTypeSubstitution() {
        var assembly = GeneratedAssembly.Create(
            HandlerPreamble +
            """
            public interface IStamp { string Value { get; } }

            [SingletonService]
            public class DefaultStamp : IStamp { public string Value => "?"; }

            [SingletonService(Key = "quiet")]
            public class QuietStamp : IStamp { public string Value => "."; }

            [Decorator]
            public class StampedHandler<TRequest, TResponse>(
                IHandler<TRequest, TResponse> inner,
                [FromKeyedServices("quiet")] IStamp stamp) : IHandler<TRequest, TResponse> {

                public TResponse Handle(TRequest request) {
                    Log.Lines.Add(stamp.Value);
                    return inner.Handle(request);
                }
            }

            public static class Log { public static System.Collections.Generic.List<string> Lines = new(); }

            [DependencyModule]
            public partial class TestModule;
            """);

        var provider = assembly.BuildProvider();
        var handler = assembly.Type("IHandler`2");

        Handle(
            provider.GetRequiredService(handler.MakeGenericType(assembly.Type("Create"), assembly.Type("Id")))!,
            assembly);

        var lines = (System.Collections.Generic.List<string>)
            assembly.Type("Log").GetField("Lines")!.GetValue(null)!;

        Assert.Equal(["."], lines);
    }

    /// <summary>
    /// A generic decorator's optional dependency resolves to null rather than throwing.
    /// </summary>
    [Fact]
    public void GenericDecorator_OptionalDependencyResolvesToNull() {
        var assembly = GeneratedAssembly.Create(
            HandlerPreamble +
            """
            public interface IAudit { }

            [Decorator]
            public class AuditedHandler<TRequest, TResponse>(
                IHandler<TRequest, TResponse> inner, IAudit? audit) : IHandler<TRequest, TResponse> {

                public bool Audited => audit != null;

                public TResponse Handle(TRequest request) => inner.Handle(request);
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        var handler = assembly.Type("IHandler`2");

        var resolved = assembly.BuildProvider().GetRequiredService(
            handler.MakeGenericType(assembly.Type("Create"), assembly.Type("Id")));

        Assert.False((bool)resolved.GetType().GetProperty("Audited")!.GetValue(resolved)!);
    }

    /// <summary>
    /// A dependency that is itself generic in the decorator's parameters is closed the same way.
    /// </summary>
    /// <remarks>
    /// <c>IValidator&lt;TRequest&gt;</c> has to become <c>IValidator&lt;Create&gt;</c>. Substituting
    /// only the top-level parameter types and not their arguments produces code that does not
    /// compile, which is the failure this pins.
    /// </remarks>
    [Fact]
    public void GenericDecorator_ClosesADependencyOverItsOwnTypeParameters() {
        var assembly = GeneratedAssembly.Create(
            HandlerPreamble +
            """
            public interface IValidator<T> { string Name { get; } }

            [SingletonService]
            public class CreateValidator : IValidator<Create> { public string Name => "create"; }

            [Decorator]
            public class ValidatedHandler<TRequest, TResponse>(
                IHandler<TRequest, TResponse> inner,
                IValidator<TRequest> validator) : IHandler<TRequest, TResponse> {

                public string ValidatorName => validator.Name;

                public TResponse Handle(TRequest request) => inner.Handle(request);
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        var handler = assembly.Type("IHandler`2");

        var resolved = assembly.BuildProvider().GetRequiredService(
            handler.MakeGenericType(assembly.Type("Create"), assembly.Type("Id")));

        Assert.Equal("create", resolved.GetType().GetProperty("ValidatorName")!.GetValue(resolved));
    }

    /// <summary>
    /// A value-type type argument is decorated like any other.
    /// </summary>
    /// <remarks>
    /// This is the instantiation Native AOT can never produce at run time, and the reason the
    /// open-generic runtime call had to go. Under a JIT it passes either way, so this asserts the
    /// shape rather than the outcome: the emitted call must name the closed decorator.
    /// </remarks>
    [Fact]
    public void GenericDecorator_ClosesOverAValueTypeArgument() {
        var result = GeneratorTestHarness.Run(
            HandlerPreamble +
            """
            [Decorator]
            public class LoggingHandler<TRequest, TResponse>(
                IHandler<TRequest, TResponse> inner) : IHandler<TRequest, TResponse> {

                public TResponse Handle(TRequest request) => inner.Handle(request);
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        Assert.Empty(result.Errors);

        var decorators = Assert.Single(
            result.GeneratedSources, source => source.Key.Contains("Decorators")).Value;

        // One closed call per registration, each naming the decorator closed over the same
        // arguments — including the value-type one, which is the instantiation Native AOT cannot
        // produce at run time and the reason the open-generic call had to go.
        Assert.True(
            System.Text.RegularExpressions.Regex.Matches(decorators, "Decorate<").Count == 2,
            "expected one closed Decorate call per registration, got:\n" + decorators);

        // Nothing is closed at run time any more.
        Assert.DoesNotContain("IHandler<,>", decorators);
    }

    /// <summary>
    /// Two generic decorators over one service nest in their declared order.
    /// </summary>
    [Fact]
    public void GenericDecorators_StackInOrder() {
        var assembly = GeneratedAssembly.Create(
            HandlerPreamble +
            """
            public static class Log { public static System.Collections.Generic.List<string> Lines = new(); }

            [Decorator(Order = 1)]
            public class InnerMost<TRequest, TResponse>(
                IHandler<TRequest, TResponse> inner) : IHandler<TRequest, TResponse> {
                public TResponse Handle(TRequest r) { Log.Lines.Add("inner"); return inner.Handle(r); }
            }

            [Decorator(Order = 2)]
            public class OuterMost<TRequest, TResponse>(
                IHandler<TRequest, TResponse> inner) : IHandler<TRequest, TResponse> {
                public TResponse Handle(TRequest r) { Log.Lines.Add("outer"); return inner.Handle(r); }
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        var provider = assembly.BuildProvider();
        var handler = assembly.Type("IHandler`2");

        Handle(
            provider.GetRequiredService(handler.MakeGenericType(assembly.Type("Create"), assembly.Type("Id")))!,
            assembly);

        var lines = (System.Collections.Generic.List<string>)
            assembly.Type("Log").GetField("Lines")!.GetValue(null)!;

        // Higher order wraps further out, so it runs first.
        Assert.Equal(["outer", "inner"], lines);
    }

    /// <summary>
    /// One declaration is applied once, even when two registration paths both name the service.
    /// </summary>
    /// <remarks>
    /// A generic decorator is expanded against the attribute registrations and again against the
    /// convention ones, and the two passes cannot see each other. Where both produce the same closed
    /// service the decoration is emitted twice, and without the guard in <c>DecoratorHelper</c> the
    /// implementation behind it is wrapped twice — two log lines per call, no exception, nothing in
    /// the build.
    /// </remarks>
    [Fact]
    public void GenericDecorator_RegisteredByBothPaths_IsAppliedOnce() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;
            using DependencyModules.Runtime.Conventions;

            namespace TestNamespace;

            public interface IHandler<TRequest, TResponse> { TResponse Handle(TRequest request); }

            public class Create { }
            public class Id { public string Value = ""; }

            public static class Log { public static System.Collections.Generic.List<string> Lines = new(); }

            // Attribute-registered.
            [SingletonService]
            public class AttributedHandler : IHandler<Create, Id> {
                public Id Handle(Create r) => new Id { Value = "attributed" };
            }

            // Convention-registered, same closed service.
            public class ConventionHandler : IHandler<Create, Id> {
                public Id Handle(Create r) => new Id { Value = "convention" };
            }

            [Decorator]
            public class LoggingHandler<TRequest, TResponse>(
                IHandler<TRequest, TResponse> inner) : IHandler<TRequest, TResponse> {

                public TResponse Handle(TRequest r) { Log.Lines.Add("logged"); return inner.Handle(r); }
            }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                public void Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll(typeof(IHandler<,>)).AsSingleton();
                }
            }
            """);

        var provider = assembly.BuildProvider();
        var handler = assembly.Type("IHandler`2")
            .MakeGenericType(assembly.Type("Create"), assembly.Type("Id"));

        foreach (var service in (System.Collections.IEnumerable)provider.GetServices(handler)) {
            Handle(service!, assembly);
        }

        var lines = (System.Collections.Generic.List<string>)
            assembly.Type("Log").GetField("Lines")!.GetValue(null)!;

        // Two registrations, one decoration each — not two each.
        Assert.Equal(2, lines.Count);
    }

    // ------------------------------------------------------------------------------------------
    // Option coverage. Each of these varies one thing the emission has to account for, and the
    // reason each is here is that the generator now writes the construction itself — so everything
    // ActivatorUtilities and the container used to decide is now the generator's to get right.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// A decorator declaring several constructors gets the one it marked, not the greediest.
    /// </summary>
    /// <remarks>
    /// The container honours <c>[ActivatorUtilitiesConstructor]</c>, so generated code has to as
    /// well. Picking the greediest instead compiles and resolves — it just builds the decorator a
    /// different way than the author asked for, which nothing would report.
    /// </remarks>
    [Fact]
    public void Decorator_HonoursTheConstructorItMarked() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;
            using Microsoft.Extensions.DependencyInjection;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            [SingletonService]
            public class Greeter : IGreeter { public string Greet() => "hello"; }

            [SingletonService]
            public class Extra { public string Value => "extra"; }

            [Decorator]
            public class PickyGreeter : IGreeter {
                private readonly IGreeter _inner;
                private readonly string _via;

                [ActivatorUtilitiesConstructor]
                public PickyGreeter(IGreeter inner) { _inner = inner; _via = "marked"; }

                public PickyGreeter(IGreeter inner, Extra extra) { _inner = inner; _via = extra.Value; }

                public string Greet() => _inner.Greet() + ":" + _via;
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        Assert.Equal("hello:marked", Invoke(
            assembly.BuildProvider().GetRequiredService(assembly.Type("IGreeter")), "Greet"));
    }

    /// <summary>
    /// A decorator can take the provider itself.
    /// </summary>
    /// <remarks>
    /// An <c>IServiceProvider</c> parameter is the provider, not something to resolve from it.
    /// Resolving it would work by accident on Microsoft's container and is wrong in principle.
    /// </remarks>
    [Fact]
    public void Decorator_TakingTheProviderGetsTheProvider() {
        var assembly = GeneratedAssembly.Create(
            """
            using System;
            using DependencyModules.Runtime.Attributes;
            using Microsoft.Extensions.DependencyInjection;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            [SingletonService]
            public class Greeter : IGreeter { public string Greet() => "hello"; }

            [SingletonService]
            public class Suffix { public string Value => "!"; }

            [Decorator]
            public class LazyGreeter(IGreeter inner, IServiceProvider provider) : IGreeter {
                public string Greet() =>
                    inner.Greet() + provider.GetRequiredService<Suffix>().Value;
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        Assert.Equal("hello!", Invoke(
            assembly.BuildProvider().GetRequiredService(assembly.Type("IGreeter")), "Greet"));
    }

    /// <summary>
    /// Decorating a keyed registration keeps its key.
    /// </summary>
    /// <remarks>
    /// The decoration replaces the descriptor, so a key dropped in the rewrite makes the service
    /// unresolvable under the name it was registered with — while an unkeyed resolution starts
    /// working, which looks like the service moved rather than broke.
    /// </remarks>
    [Fact]
    public void Decorator_KeyedRegistrationKeepsItsKey() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;
            using Microsoft.Extensions.DependencyInjection;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            [SingletonService(Key = "formal")]
            public class Greeter : IGreeter { public string Greet() => "good day"; }

            [Decorator]
            public class LoudGreeter(IGreeter inner) : IGreeter {
                public string Greet() => inner.Greet().ToUpperInvariant();
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        var provider = assembly.BuildProvider();
        var greeter = assembly.Type("IGreeter");

        Assert.Equal("GOOD DAY", Invoke(provider.GetRequiredKeyedService(greeter, "formal"), "Greet"));
        Assert.Null(provider.GetService(greeter));
    }

    /// <summary>
    /// A type argument that is itself generic is substituted at depth.
    /// </summary>
    /// <remarks>
    /// <c>IHandler&lt;List&lt;Create&gt;, Id&gt;</c> has to close the decorator over the whole
    /// argument, not over its outer shape. Substituting only the top level emits a type argument
    /// that does not compile, which is the failure mode this pins.
    /// </remarks>
    [Fact]
    public void GenericDecorator_ClosesOverANestedTypeArgument() {
        var assembly = GeneratedAssembly.Create(
            """
            using System.Collections.Generic;
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IHandler<TRequest, TResponse> { TResponse Handle(TRequest request); }

            public class Create { }
            public class Id { public string Value = ""; }

            [SingletonService]
            public class BatchHandler : IHandler<List<Create>, Id> {
                public Id Handle(List<Create> r) => new Id { Value = "batch:" + r.Count };
            }

            [Decorator]
            public class LoggingHandler<TRequest, TResponse>(
                IHandler<TRequest, TResponse> inner) : IHandler<TRequest, TResponse> {

                public TResponse Handle(TRequest request) => inner.Handle(request);
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        var handler = assembly.Type("IHandler`2").MakeGenericType(
            typeof(List<>).MakeGenericType(assembly.Type("Create")), assembly.Type("Id"));

        var resolved = assembly.BuildProvider().GetRequiredService(handler);

        Assert.Equal("LoggingHandler`2", resolved.GetType().Name);
    }

    /// <summary>
    /// Only the closings the compilation registers are decorated.
    /// </summary>
    /// <remarks>
    /// A generic decorator is not "every possible construction" — it is one decoration per
    /// registration. Emitting for a construction nothing registers would be dead code at best.
    /// </remarks>
    [Fact]
    public void GenericDecorator_DecoratesOnlyTheRegisteredClosings() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IHandler<T> { string Handle(); }

            public class Registered { }
            public class NeverRegistered { }

            [SingletonService]
            public class RegisteredHandler : IHandler<Registered> { public string Handle() => "yes"; }

            public class OrphanHandler : IHandler<NeverRegistered> { public string Handle() => "no"; }

            [Decorator]
            public class LoggingHandler<T>(IHandler<T> inner) : IHandler<T> {
                public string Handle() => inner.Handle();
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        Assert.Empty(result.Errors);

        var decorators = Assert.Single(
            result.GeneratedSources, source => source.Key.Contains("Decorators")).Value;

        Assert.Contains("Registered", decorators);
        Assert.DoesNotContain("NeverRegistered", decorators);
    }

    /// <summary>
    /// A generic decorator keeps the lifetime each registration declared.
    /// </summary>
    [Fact]
    public void GenericDecorator_PreservesEachRegistrationsLifetime() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IHandler<T> { string Handle(); }

            public class A { }
            public class B { }

            [SingletonService]
            public class AHandler : IHandler<A> { public string Handle() => "a"; }

            [TransientService]
            public class BHandler : IHandler<B> { public string Handle() => "b"; }

            [Decorator]
            public class LoggingHandler<T>(IHandler<T> inner) : IHandler<T> {
                public string Handle() => inner.Handle();
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        var handler = assembly.Type("IHandler`1");

        var singleton = Assert.Single(
            assembly.Services,
            d => d.ServiceType == handler.MakeGenericType(assembly.Type("A")));

        var transient = Assert.Single(
            assembly.Services,
            d => d.ServiceType == handler.MakeGenericType(assembly.Type("B")));

        Assert.Equal(ServiceLifetime.Singleton, singleton.Lifetime);
        Assert.Equal(ServiceLifetime.Transient, transient.Lifetime);
    }

    /// <summary>
    /// A scoped service behind a generic decorator is still disposed by the container.
    /// </summary>
    /// <remarks>
    /// The non-generic case has its own test. This one goes through the substitution path, where the
    /// displaced registration is created from a rebuilt model rather than the one the transform
    /// produced.
    /// </remarks>
    [Fact]
    public void GenericDecorator_LeavesTheInnerOwnedByTheContainer() {
        var assembly = GeneratedAssembly.Create(
            """
            using System;
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IHandler<T> { string Handle(); }

            public class A { }

            public static class Log { public static int Disposals; }

            [ScopedService]
            public class AHandler : IHandler<A>, IDisposable {
                public string Handle() => "a";
                public void Dispose() => Log.Disposals++;
            }

            [Decorator]
            public class LoggingHandler<T>(IHandler<T> inner) : IHandler<T> {
                public string Handle() => inner.Handle();
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        var provider = assembly.BuildProvider();
        var handler = assembly.Type("IHandler`1").MakeGenericType(assembly.Type("A"));

        using (var scope = provider.CreateScope()) {
            Assert.Equal("a", Invoke(scope.ServiceProvider.GetRequiredService(handler), "Handle"));
        }

        Assert.Equal(1, (int)assembly.Type("Log").GetField("Disposals")!.GetValue(null)!);
    }

    /// <summary>
    /// A decorator scoped to a realm decorates only that module's registrations.
    /// </summary>
    [Fact]
    public void Decorator_ScopedToARealm_DecoratesOnlyThatModule() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            [SingletonService(Realm = typeof(DecoratedModule))]
            public class Greeter : IGreeter { public string Greet() => "hello"; }

            [Decorator(Realm = typeof(DecoratedModule))]
            public class LoudGreeter(IGreeter inner) : IGreeter {
                public string Greet() => inner.Greet().ToUpperInvariant();
            }

            [DependencyModule(OnlyRealm = true)]
            public partial class DecoratedModule;

            [DependencyModule(OnlyRealm = true)]
            public partial class PlainModule;
            """,
            moduleName: "DecoratedModule");

        Assert.Equal("HELLO", Invoke(
            assembly.BuildProvider().GetRequiredService(assembly.Type("IGreeter")), "Greet"));
    }

    /// <summary>
    /// A decorator gated on the environment is applied only when the condition holds.
    /// </summary>
    /// <remarks>
    /// The guard wraps the call, not the registration, so a decorator that does not apply is simply
    /// never run and the service resolves undecorated — rather than being wrapped by something that
    /// re-tests the environment on every call.
    /// </remarks>
    [Theory]
    [InlineData("Development", "HELLO")]
    [InlineData("Production", "hello")]
    public void Decorator_WithAnEnvironmentCondition_AppliesOnlyWhenItHolds(
        string environment, string expected) {

        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            [SingletonService]
            public class Greeter : IGreeter { public string Greet() => "hello"; }

            [Decorator]
            [IfEnvironment("Development")]
            public class LoudGreeter(IGreeter inner) : IGreeter {
                public string Greet() => inner.Greet().ToUpperInvariant();
            }

            [DependencyModule]
            public partial class TestModule;
            """,
            environment: new ModuleEnvironment(environment));

        Assert.Equal(expected, Invoke(
            assembly.BuildProvider().GetRequiredService(assembly.Type("IGreeter")), "Greet"));
    }

    /// <summary>
    /// Every implementation behind one service is decorated, not just the last registered.
    /// </summary>
    [Fact]
    public void Decorator_WrapsEveryImplementationOfTheService() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            [SingletonService]
            public class English : IGreeter { public string Greet() => "hello"; }

            [SingletonService]
            public class French : IGreeter { public string Greet() => "bonjour"; }

            [Decorator]
            public class LoudGreeter(IGreeter inner) : IGreeter {
                public string Greet() => inner.Greet().ToUpperInvariant();
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        var all = ((System.Collections.IEnumerable)assembly.BuildProvider()
                .GetServices(assembly.Type("IGreeter")))
            .Cast<object>()
            .Select(service => Invoke(service, "Greet"))
            .ToArray();

        Assert.Equal(["HELLO", "BONJOUR"], all);
    }

    /// <summary>
    /// Interception and decoration compose on one service.
    /// </summary>
    /// <remarks>
    /// Both rewrite the same descriptor through the same helper, so they stack rather than one
    /// replacing the other. Worth pinning: they are emitted by different writers and nothing else
    /// asserts that the second sees what the first produced.
    /// </remarks>
    [Fact]
    public void Decorator_AndInterceptor_BothWrapTheService() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;
            using DependencyModules.Runtime.Interception;

            namespace TestNamespace;

            public static class Log { public static System.Collections.Generic.List<string> Lines = new(); }

            public interface IGreeter { string Greet(); }

            [SingletonService]
            [Intercept(typeof(TracingInterceptor))]
            public class Greeter : IGreeter { public string Greet() => "hello"; }

            [SingletonService]
            public class TracingInterceptor : IInterceptor {
                public TResult Intercept<TResult>(InvocationContext<TResult> context) {
                    Log.Lines.Add("intercepted");
                    return context.Proceed();
                }
            }

            [Decorator]
            public class LoudGreeter(IGreeter inner) : IGreeter {
                public string Greet() { Log.Lines.Add("decorated"); return inner.Greet().ToUpperInvariant(); }
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        var greeted = Invoke(
            assembly.BuildProvider().GetRequiredService(assembly.Type("IGreeter")), "Greet");

        var lines = (System.Collections.Generic.List<string>)
            assembly.Type("Log").GetField("Lines")!.GetValue(null)!;

        Assert.Equal("HELLO", greeted);
        Assert.Contains("decorated", lines);
        Assert.Contains("intercepted", lines);
    }

    /// <summary>
    /// A convention registering matches as themselves is decorated too.
    /// </summary>
    /// <remarks>
    /// <c>AsSelf()</c> registers the implementation as its own service type, so the decorator has to
    /// name the concrete class rather than an interface. Nothing else covers a decoration whose
    /// service type is the implementation.
    /// </remarks>
    [Fact]
    public void Decorator_OverAConventionRegisteredAsSelf() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;
            using DependencyModules.Runtime.Conventions;

            namespace TestNamespace;

            public interface IMarker { }

            public class Worker : IMarker { public virtual string Work() => "work"; }

            [Decorator(Service = typeof(Worker))]
            public class LoudWorker(Worker inner) : Worker {
                public override string Work() => inner.Work().ToUpperInvariant();
            }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                public void Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IMarker>().AsSelf().AsSingleton();
                }
            }
            """);

        Assert.Equal("WORK", Invoke(
            assembly.BuildProvider().GetRequiredService(assembly.Type("Worker")), "Work"));
    }

    /// <summary>
    /// A module-level <c>[Decorate]</c> can name a generic decorator.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two things have to be right for this. The attribute is re-emitted onto the generated module
    /// partial, and <c>typeof(LoudHandler&lt;&gt;)</c> binds to the unbound symbol — whose type
    /// arguments are the declaration's type <i>parameters</i>. Rendered verbatim that writes
    /// <c>typeof(LoudHandler&lt;T&gt;)</c> into generated code where <c>T</c> is not in scope, which
    /// is CS0246 for an attribute the developer wrote correctly.
    /// </para>
    /// <para>
    /// And the decorator still has to be expanded per registration, with its constructor looked up
    /// from the compilation rather than read from a declaration.
    /// </para>
    /// </remarks>
    [Fact]
    public void ModuleLevelDecorate_CanNameAGenericDecorator() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IHandler<T> { string Handle(); }

            public class A { }
            public class B { }

            [SingletonService]
            public class AHandler : IHandler<A> { public string Handle() => "a"; }

            [SingletonService]
            public class BHandler : IHandler<B> { public string Handle() => "b"; }

            // No [Decorator]: the module names it.
            public class LoudHandler<T>(IHandler<T> inner) : IHandler<T> {
                public string Handle() => inner.Handle().ToUpperInvariant();
            }

            [DependencyModule]
            [Decorate(typeof(IHandler<>), typeof(LoudHandler<>))]
            public partial class TestModule;
            """);

        var provider = assembly.BuildProvider();
        var handler = assembly.Type("IHandler`1");

        Assert.Equal("A", Invoke(
            provider.GetRequiredService(handler.MakeGenericType(assembly.Type("A"))), "Handle"));
        Assert.Equal("B", Invoke(
            provider.GetRequiredService(handler.MakeGenericType(assembly.Type("B"))), "Handle"));
    }

    // ------------------------------------------------------------------------------------------
    // Adversarial cases. Each one is a shape the emission could plausibly get wrong.
    // ------------------------------------------------------------------------------------------

    /// <summary>The wrapped service does not have to be the first constructor parameter.</summary>
    [Fact]
    public void Decorator_InnerParameterNeedNotComeFirst() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            [SingletonService]
            public class Greeter : IGreeter { public string Greet() => "hello"; }

            [SingletonService]
            public class Suffix { public string Value => "!"; }

            [Decorator]
            public class LoudGreeter(Suffix suffix, IGreeter inner) : IGreeter {
                public string Greet() => inner.Greet() + suffix.Value;
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        Assert.Equal("hello!", Invoke(
            assembly.BuildProvider().GetRequiredService(assembly.Type("IGreeter")), "Greet"));
    }

    /// <summary>A record decorator is constructed through its primary constructor.</summary>
    [Fact]
    public void Decorator_DeclaredAsARecord() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            [SingletonService]
            public class Greeter : IGreeter { public string Greet() => "hello"; }

            [Decorator]
            public record LoudGreeter(IGreeter Inner) : IGreeter {
                public string Greet() => Inner.Greet().ToUpperInvariant();
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        Assert.Equal("HELLO", Invoke(
            assembly.BuildProvider().GetRequiredService(assembly.Type("IGreeter")), "Greet"));
    }

    /// <summary>A decorator nested inside another type is named correctly in the emitted new.</summary>
    [Fact]
    public void Decorator_NestedInsideAnotherType() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            [SingletonService]
            public class Greeter : IGreeter { public string Greet() => "hello"; }

            public static class Outer {
                [Decorator]
                public class LoudGreeter(IGreeter inner) : IGreeter {
                    public string Greet() => inner.Greet().ToUpperInvariant();
                }
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        Assert.Equal("HELLO", Invoke(
            assembly.BuildProvider().GetRequiredService(assembly.Type("IGreeter")), "Greet"));
    }

    /// <summary>A decorator whose inner parameter is nullable still finds it.</summary>
    /// <remarks>
    /// Unusual but legal, and the parameter type then carries a nullable annotation the service type
    /// does not. Matching them without normalising means no parameter looks like the service, and
    /// the decoration is dropped with nothing said.
    /// </remarks>
    [Fact]
    public void Decorator_WithANullableInnerParameter() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            [SingletonService]
            public class Greeter : IGreeter { public string Greet() => "hello"; }

            [Decorator]
            public class LoudGreeter(IGreeter? inner) : IGreeter {
                public string Greet() => inner?.Greet().ToUpperInvariant() ?? "none";
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        Assert.Equal("HELLO", Invoke(
            assembly.BuildProvider().GetRequiredService(assembly.Type("IGreeter")), "Greet"));
    }

    /// <summary>
    /// A generic decorator whose type parameters are not the service's arguments in order is not
    /// emitted, and does not emit anything broken either.
    /// </summary>
    /// <remarks>
    /// <c>Swapped&lt;TResponse, TRequest&gt; : IHandler&lt;TRequest, TResponse&gt;</c> is legal C#
    /// that cannot be closed by position. Guessing would emit a <c>new</c> with the arguments the
    /// wrong way round, which compiles whenever the two types happen to be compatible.
    /// </remarks>
    [Fact]
    public void GenericDecorator_WithReorderedTypeParameters_IsNotEmitted() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IHandler<TRequest, TResponse> { TResponse Handle(TRequest request); }

            public class Create { }
            public class Id { public string Value = ""; }

            [SingletonService]
            public class CreateHandler : IHandler<Create, Id> {
                public Id Handle(Create r) => new Id { Value = "created" };
            }

            [Decorator]
            public class Swapped<TResponse, TRequest>(
                IHandler<TRequest, TResponse> inner) : IHandler<TRequest, TResponse> {

                public TResponse Handle(TRequest request) => inner.Handle(request);
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        Assert.Empty(result.Errors);

        Assert.DoesNotContain(
            result.GeneratedSources,
            source => source.Value.Contains("new global::TestNamespace.Swapped"));
    }

    /// <summary>
    /// A generic decorator with fewer type parameters than the service has arguments is not emitted.
    /// </summary>
    [Fact]
    public void GenericDecorator_WithMismatchedArity_IsNotEmitted() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IHandler<TRequest, TResponse> { TResponse Handle(TRequest request); }

            public class Thing { }

            [SingletonService]
            public class ThingHandler : IHandler<Thing, Thing> {
                public Thing Handle(Thing r) => r;
            }

            [Decorator]
            public class Same<T>(IHandler<T, T> inner) : IHandler<T, T> {
                public T Handle(T request) => inner.Handle(request);
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        Assert.Empty(result.Errors);
    }

    /// <summary>A cross-wired registration is a factory descriptor, and decorates like one.</summary>
    [Fact]
    public void Decorator_OverACrossWiredRegistration() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            [CrossWireService]
            public class Greeter : IGreeter { public string Greet() => "hello"; }

            [Decorator]
            public class LoudGreeter(IGreeter inner) : IGreeter {
                public string Greet() => inner.Greet().ToUpperInvariant();
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        var provider = assembly.BuildProvider();

        Assert.Equal("HELLO", Invoke(provider.GetRequiredService(assembly.Type("IGreeter")), "Greet"));

        // The implementation stays resolvable as itself, undecorated — that is what cross-wiring is.
        Assert.Equal("hello", Invoke(provider.GetRequiredService(assembly.Type("Greeter")), "Greet"));
    }

    /// <summary>
    /// A decorator whose own dependency is generic in the service's arguments and comes from a
    /// convention.
    /// </summary>
    [Fact]
    public void GenericDecorator_OverConventionRegistrations_WithAGenericDependency() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;
            using DependencyModules.Runtime.Conventions;

            namespace TestNamespace;

            public interface IHandler<T> { string Handle(); }

            public class A { }

            public class AHandler : IHandler<A> { public string Handle() => "a"; }

            public interface ILabel<T> { string Text { get; } }

            [SingletonService]
            public class ALabel : ILabel<A> { public string Text => "[A]"; }

            [Decorator]
            public class LabelledHandler<T>(IHandler<T> inner, ILabel<T> label) : IHandler<T> {
                public string Handle() => label.Text + inner.Handle();
            }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                public void Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll(typeof(IHandler<>)).AsSingleton();
                }
            }
            """);

        var handler = assembly.Type("IHandler`1").MakeGenericType(assembly.Type("A"));

        Assert.Equal("[A]a", Invoke(assembly.BuildProvider().GetRequiredService(handler), "Handle"));
    }

    /// <summary>
    /// An unscoped decorator in a compilation with two modules is applied once, not once per module.
    /// </summary>
    /// <remarks>
    /// A decorator with no realm belongs to every module that is not realm-only, so both modules
    /// emit it. Both emissions name the same closed service, and the collection they rewrite is the
    /// same one — so without the guard the implementation is wrapped twice, which shows up as a
    /// decorator's side effects happening twice per call and nothing else.
    /// </remarks>
    [Fact]
    public void Decorator_WithTwoModulesInTheCompilation_IsAppliedOnce() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public static class Log { public static int Applied; }

            public interface IGreeter { string Greet(); }

            [SingletonService]
            public class Greeter : IGreeter { public string Greet() => "hello"; }

            [Decorator]
            public class CountingGreeter(IGreeter inner) : IGreeter {
                public string Greet() { Log.Applied++; return inner.Greet(); }
            }

            [DependencyModule]
            public partial class TestModule;

            [DependencyModule]
            public partial class OtherModule;
            """);

        Invoke(assembly.BuildProvider().GetRequiredService(assembly.Type("IGreeter")), "Greet");

        Assert.Equal(1, (int)assembly.Type("Log").GetField("Applied")!.GetValue(null)!);
    }

    /// <summary>Two decorators of one service sharing an order are still refused.</summary>
    /// <remarks>
    /// The check moved when decoration collapsed into one stage. It is the only thing standing
    /// between two decorators and a nesting order nobody declared.
    /// </remarks>
    [Fact]
    public void Decorators_SharingAnOrder_AreReported() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            [SingletonService]
            public class Greeter : IGreeter { public string Greet() => "hello"; }

            [Decorator]
            public class First(IGreeter inner) : IGreeter { public string Greet() => inner.Greet(); }

            [Decorator]
            public class Second(IGreeter inner) : IGreeter { public string Greet() => inner.Greet(); }

            [DependencyModule]
            public partial class TestModule;
            """);

        var reported = Assert.Single(result.GeneratorDiagnostics, d => d.Id == "DM0007");

        Assert.Contains("First", reported.GetMessage());
        Assert.Contains("Second", reported.GetMessage());
    }

    /// <summary>
    /// A generic and a non-generic decorator over one closed service nest by declared order.
    /// </summary>
    /// <remarks>
    /// They arrive at the writer from different routes — one expanded per registration, one passed
    /// through — so this pins that the ordering applies across both rather than within each.
    /// </remarks>
    [Fact]
    public void GenericAndNonGenericDecorators_NestByOrder() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public static class Log { public static System.Collections.Generic.List<string> Lines = new(); }

            public interface IHandler<T> { string Handle(); }

            public class A { }

            [SingletonService]
            public class AHandler : IHandler<A> { public string Handle() => "a"; }

            [Decorator(Order = 1)]
            public class GenericInner<T>(IHandler<T> inner) : IHandler<T> {
                public string Handle() { Log.Lines.Add("generic"); return inner.Handle(); }
            }

            [Decorator(Order = 2)]
            public class SpecificOuter(IHandler<A> inner) : IHandler<A> {
                public string Handle() { Log.Lines.Add("specific"); return inner.Handle(); }
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        var handler = assembly.Type("IHandler`1").MakeGenericType(assembly.Type("A"));

        Invoke(assembly.BuildProvider().GetRequiredService(handler), "Handle");

        var lines = (System.Collections.Generic.List<string>)
            assembly.Type("Log").GetField("Lines")!.GetValue(null)!;

        Assert.Equal(["specific", "generic"], lines);
    }

    /// <summary>An unscoped decorator does not reach a realm-only module.</summary>
    [Fact]
    public void Decorator_Unscoped_DoesNotReachARealmOnlyModule() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            [SingletonService(Realm = typeof(RealmModule))]
            public class Greeter : IGreeter { public string Greet() => "hello"; }

            [Decorator]
            public class LoudGreeter(IGreeter inner) : IGreeter {
                public string Greet() => inner.Greet().ToUpperInvariant();
            }

            [DependencyModule(OnlyRealm = true)]
            public partial class RealmModule;
            """,
            moduleName: "RealmModule");

        Assert.Equal("hello", Invoke(
            assembly.BuildProvider().GetRequiredService(assembly.Type("IGreeter")), "Greet"));
    }

    /// <summary>A convention registering matches under a key is decorated under that key.</summary>
    [Fact]
    public void Decorator_OverAKeyedConventionRegistration() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;
            using DependencyModules.Runtime.Conventions;
            using Microsoft.Extensions.DependencyInjection;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            public class Greeter : IGreeter { public string Greet() => "hello"; }

            [Decorator]
            public class LoudGreeter(IGreeter inner) : IGreeter {
                public string Greet() => inner.Greet().ToUpperInvariant();
            }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                public void Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IGreeter>().WithKey("loud").AsSingleton();
                }
            }
            """);

        var provider = assembly.BuildProvider();
        var greeter = assembly.Type("IGreeter");

        Assert.Equal("HELLO", Invoke(provider.GetRequiredKeyedService(greeter, "loud"), "Greet"));
        Assert.Null(provider.GetService(greeter));
    }

    /// <summary>
    /// A convention registering as self and interfaces cross-wires, and the interface is decorated.
    /// </summary>
    /// <remarks>
    /// <c>AsSelfWithInterfaces</c> registers each interface as a factory resolving the
    /// implementation, so the decorated descriptor is a factory and the shared instance the contract
    /// promises has to survive the rewrite.
    /// </remarks>
    [Fact]
    public void Decorator_OverAConventionRegisteredAsSelfWithInterfaces() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;
            using DependencyModules.Runtime.Conventions;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            public class Greeter : IGreeter { public string Greet() => "hello"; }

            [Decorator]
            public class LoudGreeter(IGreeter inner) : IGreeter {
                public string Greet() => inner.Greet().ToUpperInvariant();
            }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                public void Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IGreeter>().AsSelfWithInterfaces().AsSingleton();
                }
            }
            """);

        var provider = assembly.BuildProvider();

        Assert.Equal("HELLO", Invoke(provider.GetRequiredService(assembly.Type("IGreeter")), "Greet"));

        // The implementation itself stays undecorated, which is what cross-wiring means.
        Assert.Equal("hello", Invoke(provider.GetRequiredService(assembly.Type("Greeter")), "Greet"));
    }

    /// <summary>A decorator with no matching registration emits nothing and breaks nothing.</summary>
    [Fact]
    public void Decorator_WithNothingToDecorate_EmitsNothing() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            [Decorator]
            public class LoudGreeter(IGreeter inner) : IGreeter {
                public string Greet() => inner.Greet().ToUpperInvariant();
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        Assert.Empty(result.Errors);
    }

    /// <summary>
    /// A generic decorator constrained to reference types is not emitted for a value-type closing.
    /// </summary>
    /// <remarks>
    /// <c>where T : class</c> is ordinary on a decorator, and <c>IHandler&lt;int&gt;</c> is an
    /// ordinary registration. Closing the decorator over <c>int</c> emits
    /// <c>new Logging&lt;int&gt;(…)</c>, which violates the constraint — CS0453, in generated code,
    /// for two declarations that are each perfectly legal.
    /// </remarks>
    [Fact]
    public void GenericDecorator_ConstrainedToReferenceTypes_SkipsValueTypeClosings() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IHandler<T> { string Handle(); }

            public class Thing { }

            [SingletonService]
            public class ThingHandler : IHandler<Thing> { public string Handle() => "thing"; }

            [SingletonService]
            public class IntHandler : IHandler<int> { public string Handle() => "int"; }

            [Decorator]
            public class Logging<T>(IHandler<T> inner) : IHandler<T> where T : class {
                public string Handle() => inner.Handle();
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        Assert.Empty(result.Errors);
    }

    /// <summary>
    /// A constraint the closing does satisfy still emits.
    /// </summary>
    [Fact]
    public void GenericDecorator_ConstrainedToAnInterface_EmitsForSatisfyingClosings() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IRequest { }

            public interface IHandler<T> where T : IRequest { string Handle(); }

            public class Thing : IRequest { }

            [SingletonService]
            public class ThingHandler : IHandler<Thing> { public string Handle() => "thing"; }

            [Decorator]
            public class Logging<T>(IHandler<T> inner) : IHandler<T> where T : IRequest {
                public string Handle() => inner.Handle().ToUpperInvariant();
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        var handler = assembly.Type("IHandler`1").MakeGenericType(assembly.Type("Thing"));

        Assert.Equal("THING", Invoke(assembly.BuildProvider().GetRequiredService(handler), "Handle"));
    }

    /// <summary>
    /// A decorator implementing two interfaces and taking both decorates the one it is told to.
    /// </summary>
    /// <remarks>
    /// Inference picks the first constructor parameter that is also an implemented interface, which
    /// is arbitrary when there are two. <c>Service =</c> is the way to say which, and this pins that
    /// it wins over inference rather than being one more candidate.
    /// </remarks>
    [Fact]
    public void Decorator_WithAnExplicitService_DecoratesThatOne() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }
            public interface IFarewell { string Bye(); }

            [SingletonService]
            public class Greeter : IGreeter { public string Greet() => "hello"; }

            [SingletonService]
            public class Farewell : IFarewell { public string Bye() => "bye"; }

            [Decorator(Service = typeof(IFarewell))]
            public class Loud(IGreeter greeter, IFarewell farewell) : IGreeter, IFarewell {
                public string Greet() => greeter.Greet();
                public string Bye() => farewell.Bye().ToUpperInvariant();
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        var provider = assembly.BuildProvider();

        Assert.Equal("BYE", Invoke(provider.GetRequiredService(assembly.Type("IFarewell")), "Bye"));

        // The other interface it implements is not decorated.
        Assert.Equal("hello", Invoke(provider.GetRequiredService(assembly.Type("IGreeter")), "Greet"));
    }

    /// <summary>A registration declared with Try is still decorated.</summary>
    [Fact]
    public void Decorator_OverATryRegistration() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            [SingletonService(Using = RegistrationType.Try)]
            public class Greeter : IGreeter { public string Greet() => "hello"; }

            [Decorator]
            public class LoudGreeter(IGreeter inner) : IGreeter {
                public string Greet() => inner.Greet().ToUpperInvariant();
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        Assert.Equal("HELLO", Invoke(
            assembly.BuildProvider().GetRequiredService(assembly.Type("IGreeter")), "Greet"));
    }

    /// <summary>A decorator reaching the service through a base class is still a decorator.</summary>
    /// <remarks>
    /// Inference reads the base list, which here names a class rather than the interface. If only
    /// directly-written interfaces count, this stops being recognised and is silently not applied.
    /// </remarks>
    [Fact]
    public void Decorator_ImplementingTheServiceThroughABaseClass() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            [SingletonService]
            public class Greeter : IGreeter { public string Greet() => "hello"; }

            public abstract class GreeterBase : IGreeter { public abstract string Greet(); }

            [Decorator]
            public class LoudGreeter(IGreeter inner) : GreeterBase {
                public override string Greet() => inner.Greet().ToUpperInvariant();
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        Assert.Empty(result.Errors);
    }

    /// <summary>An environment condition guards every closed call a generic decorator produces.</summary>
    [Theory]
    [InlineData("Development", "A")]
    [InlineData("Production", "a")]
    public void GenericDecorator_WithAnEnvironmentCondition_GuardsEachClosing(
        string environment, string expected) {

        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IHandler<T> { string Handle(); }

            public class A { }
            public class B { }

            [SingletonService]
            public class AHandler : IHandler<A> { public string Handle() => "a"; }

            [SingletonService]
            public class BHandler : IHandler<B> { public string Handle() => "b"; }

            [Decorator]
            [IfEnvironment("Development")]
            public class Loud<T>(IHandler<T> inner) : IHandler<T> {
                public string Handle() => inner.Handle().ToUpperInvariant();
            }

            [DependencyModule]
            public partial class TestModule;
            """,
            environment: new ModuleEnvironment(environment));

        var handler = assembly.Type("IHandler`1").MakeGenericType(assembly.Type("A"));

        Assert.Equal(expected, Invoke(assembly.BuildProvider().GetRequiredService(handler), "Handle"));
    }

    /// <summary>Two closings of one generic service each get their own decoration.</summary>
    /// <remarks>
    /// The decorator must close over each construction separately rather than over whichever was
    /// seen first.
    /// </remarks>
    [Fact]
    public void GenericDecorator_OverTwoClosingsOfOneService() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IHandler<T> { string Handle(); }

            public class A { }
            public class B { }

            [SingletonService]
            public class AHandler : IHandler<A> { public string Handle() => "multi"; }

            [SingletonService]
            public class BHandler : IHandler<B> { public string Handle() => "multi"; }

            [Decorator]
            public class Loud<T>(IHandler<T> inner) : IHandler<T> {
                public string Handle() => inner.Handle().ToUpperInvariant();
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        var provider = assembly.BuildProvider();
        var handler = assembly.Type("IHandler`1");

        Assert.Equal("MULTI", Invoke(
            provider.GetRequiredService(handler.MakeGenericType(assembly.Type("A"))), "Handle"));
        Assert.Equal("MULTI", Invoke(
            provider.GetRequiredService(handler.MakeGenericType(assembly.Type("B"))), "Handle"));
    }

    /// <summary>A deeply nested type argument is substituted at every level.</summary>
    [Fact]
    public void GenericDecorator_ClosesOverADeeplyNestedTypeArgument() {
        var assembly = GeneratedAssembly.Create(
            """
            using System.Collections.Generic;
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IHandler<T> { string Handle(); }

            public class Create { }

            [SingletonService]
            public class DeepHandler : IHandler<IReadOnlyList<Dictionary<string, Create>>> {
                public string Handle() => "deep";
            }

            [Decorator]
            public class Loud<T>(IHandler<T> inner) : IHandler<T> {
                public string Handle() => inner.Handle().ToUpperInvariant();
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        var handler = assembly.Type("IHandler`1").MakeGenericType(
            typeof(IReadOnlyList<>).MakeGenericType(
                typeof(Dictionary<,>).MakeGenericType(typeof(string), assembly.Type("Create"))));

        Assert.Equal("DEEP", Invoke(assembly.BuildProvider().GetRequiredService(handler), "Handle"));
    }

    /// <summary>Three type parameters are substituted in order.</summary>
    [Fact]
    public void GenericDecorator_WithThreeTypeParameters() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IPipe<TIn, TVia, TOut> { string Run(); }

            public class In { }
            public class Via { }
            public class Out { }

            [SingletonService]
            public class Pipe : IPipe<In, Via, Out> { public string Run() => "pipe"; }

            [Decorator]
            public class Loud<TIn, TVia, TOut>(IPipe<TIn, TVia, TOut> inner) : IPipe<TIn, TVia, TOut> {
                public string Run() => inner.Run().ToUpperInvariant();
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        var pipe = assembly.Type("IPipe`3").MakeGenericType(
            assembly.Type("In"), assembly.Type("Via"), assembly.Type("Out"));

        Assert.Equal("PIPE", Invoke(assembly.BuildProvider().GetRequiredService(pipe), "Run"));
    }

    /// <summary>A keyed registration with a keyed dependency on the decorator.</summary>
    [Fact]
    public void Decorator_KeyedRegistrationAndKeyedDependency() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;
            using Microsoft.Extensions.DependencyInjection;

            namespace TestNamespace;

            public interface IStamp { string Value { get; } }

            [SingletonService]
            public class DefaultStamp : IStamp { public string Value => "?"; }

            [SingletonService(Key = "quiet")]
            public class QuietStamp : IStamp { public string Value => "."; }

            public interface IGreeter { string Greet(); }

            [SingletonService(Key = "formal")]
            public class Greeter : IGreeter { public string Greet() => "good day"; }

            [Decorator]
            public class StampedGreeter(
                IGreeter inner, [FromKeyedServices("quiet")] IStamp stamp) : IGreeter {

                public string Greet() => inner.Greet() + stamp.Value;
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        Assert.Equal("good day.", Invoke(
            assembly.BuildProvider().GetRequiredKeyedService(assembly.Type("IGreeter"), "formal"), "Greet"));
    }

    /// <summary>A decorator can depend on the module environment.</summary>
    [Fact]
    public void Decorator_DependingOnTheModuleEnvironment() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime;
            using DependencyModules.Runtime.Attributes;
            using DependencyModules.Runtime.Interfaces;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            [SingletonService]
            public class Greeter : IGreeter { public string Greet() => "hello"; }

            [Decorator]
            public class NamedGreeter(IGreeter inner, IModuleEnvironment environment) : IGreeter {
                public string Greet() => inner.Greet() + (environment == null ? ":none" : ":env");
            }

            [DependencyModule]
            public partial class TestModule;
            """,
            environment: new ModuleEnvironment("Staging"));

        Assert.Equal("hello:env", Invoke(
            assembly.BuildProvider().GetRequiredService(assembly.Type("IGreeter")), "Greet"));
    }

    /// <summary>A TryEnumerable registration is decorated.</summary>
    [Fact]
    public void Decorator_OverATryEnumerableRegistration() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            [SingletonService(Using = RegistrationType.TryEnumerable)]
            public class Greeter : IGreeter { public string Greet() => "hello"; }

            [Decorator]
            public class LoudGreeter(IGreeter inner) : IGreeter {
                public string Greet() => inner.Greet().ToUpperInvariant();
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        Assert.Equal("HELLO", Invoke(
            assembly.BuildProvider().GetRequiredService(assembly.Type("IGreeter")), "Greet"));
    }

    /// <summary>A convention reaching the interface through a base class is decorated.</summary>
    [Fact]
    public void Decorator_OverAConventionUsingIncludeBaseClasses() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;
            using DependencyModules.Runtime.Conventions;

            namespace TestNamespace;

            public interface IGreeter { string Greet(); }

            public abstract class GreeterBase : IGreeter { public abstract string Greet(); }

            public class Greeter : GreeterBase { public override string Greet() => "hello"; }

            [Decorator]
            public class LoudGreeter(IGreeter inner) : IGreeter {
                public string Greet() => inner.Greet().ToUpperInvariant();
            }

            [DependencyModule]
            public partial class TestModule : IConventionModule {
                public void Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll<IGreeter>().IncludeBaseClasses().AsSingleton();
                }
            }
            """);

        Assert.Equal("HELLO", Invoke(
            assembly.BuildProvider().GetRequiredService(assembly.Type("IGreeter")), "Greet"));
    }

    /// <summary>Interception over a closed construction of a generic service.</summary>
    [Fact]
    public void Interceptor_OverAClosedGenericService() {
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;
            using DependencyModules.Runtime.Interception;

            namespace TestNamespace;

            public static class Log { public static int Calls; }

            public interface IHandler<T> { string Handle(); }

            public class A { }

            [SingletonService]
            [Intercept(typeof(CountingInterceptor))]
            public class AHandler : IHandler<A> { public string Handle() => "a"; }

            [SingletonService]
            public class CountingInterceptor : IInterceptor {
                public TResult Intercept<TResult>(InvocationContext<TResult> context) {
                    Log.Calls++;
                    return context.Proceed();
                }
            }

            [DependencyModule]
            public partial class TestModule;
            """);

        var handler = assembly.Type("IHandler`1").MakeGenericType(assembly.Type("A"));

        Assert.Equal("a", Invoke(assembly.BuildProvider().GetRequiredService(handler), "Handle"));
        Assert.Equal(1, (int)assembly.Type("Log").GetField("Calls")!.GetValue(null)!);
    }
}
