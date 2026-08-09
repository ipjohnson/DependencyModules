using DependencyModules.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// Conventions and decorators together.
/// </summary>
/// <remarks>
/// A decorator implements the interface it decorates, so a convention scanning that interface used
/// to match the decorator itself. For a generic decorator that was worse than a stray registration:
/// closing nothing, it registered as the <i>open</i> generic, and decoration then refused everything
/// because an open generic registration cannot be decorated. The error blamed the open generic
/// limitation, which is a long way from the cause.
/// </remarks>
public class ConventionDecoratorTests {

    private const string Preamble =
        """
        using System.Collections.Generic;
        using DependencyModules.Runtime.Attributes;
        using DependencyModules.Conventions;

        namespace TestNamespace;

        [SingletonService]
        public class Log { public List<string> Lines { get; } = new(); }

        public interface IRequestHandler<TRequest, TResponse> { TResponse Handle(TRequest request); }

        public class CreateOrder { }
        public class RenameOrder { }
        public class OrderId { public string Value = ""; }

        public class CreateOrderHandler : IRequestHandler<CreateOrder, OrderId> {
            public OrderId Handle(CreateOrder r) => new OrderId { Value = "created" };
        }

        public class RenameOrderHandler : IRequestHandler<RenameOrder, OrderId> {
            public OrderId Handle(RenameOrder r) => new OrderId { Value = "renamed" };
        }

        [Decorator]
        public class LoggingHandler<TRequest, TResponse>(
            IRequestHandler<TRequest, TResponse> inner, Log log)
            : IRequestHandler<TRequest, TResponse> {

            public TResponse Handle(TRequest request) {
                log.Lines.Add("handling " + typeof(TRequest).Name);
                return inner.Handle(request);
            }
        }

        """;

    private static GeneratedAssembly Compile(string module) =>
        GeneratedAssembly.Create(Preamble + module, withConventions: true);

    /// <summary>
    /// One open generic decorator over every handler a convention registered — the ordinary MediatR
    /// shape, and the reason this combination has to work.
    /// </summary>
    [Fact]
    public void OneDecoratorWrapsEveryConventionRegisteredHandler() {
        var assembly = Compile(
            """
            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll(typeof(IRequestHandler<,>)).AsScoped();
                }
            }
            """);

        var provider = assembly.BuildProvider();
        var log = provider.GetRequiredService(assembly.Type("Log"));
        var lines = (List<string>)log.GetType().GetProperty("Lines")!.GetValue(log)!;

        var handler = assembly.Type("IRequestHandler`2");
        var orderId = assembly.Type("OrderId");

        foreach (var request in new[] { "CreateOrder", "RenameOrder" }) {
            var requestType = assembly.Type(request);
            var service = provider.GetRequiredService(handler.MakeGenericType(requestType, orderId));

            // Every handler resolves as the decorator, not as the implementation.
            Assert.Equal("LoggingHandler`2", service.GetType().Name);

            service.GetType().GetMethod("Handle")!.Invoke(
                service, new[] { Activator.CreateInstance(requestType) });
        }

        Assert.Equal(["handling CreateOrder", "handling RenameOrder"], lines);
    }

    /// <summary>
    /// The decorator itself is not registered as a service.
    /// </summary>
    [Fact]
    public void ADecoratorIsNotAConventionCandidate() {
        var assembly = Compile(
            """
            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll(typeof(IRequestHandler<,>)).AsScoped();
                }
            }
            """);

        // Two handlers, two registrations. The decorator rewrote them in place rather than adding
        // a third, and never registered itself as an open generic.
        Assert.Equal(2, assembly.Services.Count(d => d.ServiceType.Name == "IRequestHandler`2"));

        Assert.DoesNotContain(assembly.Services, d => d.ServiceType.IsGenericTypeDefinition);
    }

    /// <summary>
    /// The exclusion is on the declaration, so it holds however the convention selects.
    /// </summary>
    [Fact]
    public void ADecoratorIsExcludedWhenSelectedByFilterRatherThanInterface() {
        var assembly = Compile(
            """
            [DependencyModule]
            public partial class TestModule : IConventionModule {
                void IConventionModule.Conventions(IConventionDefinitions conventions) {
                    conventions.RegisterAll().WithName("*Handler").AsSelf().AsScoped();
                }
            }
            """);

        Assert.Contains(assembly.Services, d => d.ServiceType == assembly.Type("CreateOrderHandler"));
        Assert.Contains(assembly.Services, d => d.ServiceType == assembly.Type("RenameOrderHandler"));

        // LoggingHandler ends in "Handler" and would otherwise match.
        Assert.DoesNotContain(assembly.Services, d => d.ServiceType.Name == "LoggingHandler`2");
    }
}
