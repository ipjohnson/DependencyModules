using System.Reflection;
using DependencyModules.Testing.Attributes;
using DependencyModules.Testing.Attributes.Interfaces;
using DependencyModules.Testing.Impl;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DependencyModules.Tests.TestingTests;

/// <summary>
/// Drives TestParameterResolver directly, without a test framework around it.
///
/// These rules used to live inside ModuleTestCase, where the only way to reach them was to run a
/// [ModuleTest] through xUnit's whole pipeline — so a change in precedence showed up as some
/// unrelated integration test failing, if at all. The resolver is the piece an NUnit integration
/// would share, which makes its behaviour a contract rather than an implementation detail.
/// </summary>
public class TestParameterResolverTests {

    private interface IThing;

    private class Thing : IThing;

    private class Other : IThing;

    /// <summary>
    /// Takes a dependency the container has and a value it cannot possibly know, which is what
    /// [InjectValues] is for.
    /// </summary>
    private class NeedsAValue(IThing thing, string text) {
        public IThing Thing { get; } = thing;
        public string Text { get; } = text;
    }

    [Fact]
    public async Task ResolvesAServiceFromTheContainer() {
        var arguments = await Resolve(nameof(Samples.OneService), services => services.AddSingleton<IThing, Thing>());

        Assert.IsType<Thing>(Assert.Single(arguments));
    }

    /// <summary>
    /// A test asking for the container itself cannot have it resolved from the container.
    /// </summary>
    [Fact]
    public async Task ServiceProviderParameterGetsTheContainerItself() {
        var (resolver, provider) = Build(nameof(Samples.WantsTheProvider), _ => { });

        var arguments = await resolver.ResolveArgumentsAsync(provider, []);

        Assert.Same(provider, Assert.Single(arguments));
    }

    /// <summary>
    /// A data row owns the parameters it covers, so its arguments are passed through untouched even
    /// when the container could have supplied that type.
    /// </summary>
    [Fact]
    public async Task DataTakesTheLeadingParametersAndTheContainerTakesTheRest() {
        var (resolver, provider) = Build(
            nameof(Samples.DataThenService), services => services.AddSingleton<IThing, Thing>());

        var arguments = await resolver.ResolveArgumentsAsync(provider, [42]);

        Assert.Equal(2, arguments.Length);
        Assert.Equal(42, arguments[0]);
        Assert.IsType<Thing>(arguments[1]);
    }

    /// <summary>
    /// The registration a parameter attribute makes during setup is what the container resolves
    /// afterwards — the property that lets [Mock] replace a service for the whole test rather than
    /// only for the parameter holding it.
    /// </summary>
    [Fact]
    public async Task ParameterAttributeRegistrationBeatsTheModuleRegistration() {
        var arguments = await Resolve(
            nameof(Samples.RegisteringAttribute), services => services.AddSingleton<IThing, Thing>());

        Assert.IsType<Other>(Assert.Single(arguments));
    }

    /// <summary>
    /// A provider that returns null stands aside rather than forcing a null argument, so several
    /// attributes can sit on one parameter with the first that answers winning.
    /// </summary>
    [Fact]
    public async Task AProviderReturningNullDefersToTheNextOne() {
        var arguments = await Resolve(
            nameof(Samples.AbstainingThenAnswering), services => services.AddSingleton<IThing, Thing>());

        Assert.IsType<Other>(Assert.Single(arguments));
    }

    [Fact]
    public async Task ResolvesKeyedServices() {
        var arguments = await Resolve(
            nameof(Samples.Keyed), services => {
                services.AddSingleton<IThing, Thing>();
                services.AddKeyedSingleton<IThing, Other>("other");
            });

        Assert.IsType<Other>(Assert.Single(arguments));
    }

    /// <summary>
    /// An unregistered concrete type is constructed from the container, so a test can name the class
    /// under test without registering it.
    /// </summary>
    [Fact]
    public async Task ConstructsAnUnregisteredConcreteType() {
        var arguments = await Resolve(
            nameof(Samples.UnregisteredWithInjectedValue), services => services.AddSingleton<IThing, Thing>());

        var value = Assert.IsType<NeedsAValue>(Assert.Single(arguments));

        Assert.IsType<Thing>(value.Thing);
        Assert.Equal("supplied", value.Text);
    }

    /// <summary>
    /// Resolving without the setup phase would skip every parameter attribute silently, so a [Mock]
    /// parameter would hand back the real service. It fails loudly instead.
    /// </summary>
    [Fact]
    public async Task ResolvingBeforeSetupThrows() {
        var resolver = new TestParameterResolver(ContextFor(nameof(Samples.OneService)));
        var provider = new ServiceCollection().BuildServiceProvider();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => resolver.ResolveArgumentsAsync(provider, []));

        Assert.Contains(nameof(TestParameterResolver.SetupServiceCollection), exception.Message);
    }

    [Fact]
    public void SetupIsOfferedEveryParameter() {
        var services = new ServiceCollection();

        new TestParameterResolver(ContextFor(nameof(Samples.TwoRegisteringAttributes)))
            .SetupServiceCollection(services);

        Assert.Equal(2, services.Count);
    }

    // ---- harness -------------------------------------------------------------------------------

    private static async Task<object?[]> Resolve(string methodName, Action<IServiceCollection> configure) {
        var (resolver, provider) = Build(methodName, configure);

        return await resolver.ResolveArgumentsAsync(provider, []);
    }

    private static (TestParameterResolver Resolver, IServiceProvider Provider) Build(
        string methodName, Action<IServiceCollection> configure) {
        var services = new ServiceCollection();
        var resolver = new TestParameterResolver(ContextFor(methodName));

        // Module registrations land before the parameters get their say, as they do in a real run.
        configure(services);
        resolver.SetupServiceCollection(services);

        return (resolver, services.BuildServiceProvider());
    }

    private static ITestMethodContext ContextFor(string methodName) =>
        new StubContext(typeof(Samples).GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)!);

    private class StubContext(MethodInfo method) : ITestMethodContext {
        public MethodInfo Method { get; } = method;
        public IReadOnlyList<Attribute> Attributes { get; } = [];
    }

    /// <summary>
    /// Signatures only — never invoked. Static so nothing needs constructing; the members are public
    /// within this private class so one set of binding flags finds them all.
    /// </summary>
    private static class Samples {
        public static void OneService(IThing thing) { }

        public static void WantsTheProvider(IServiceProvider provider) { }

        public static void DataThenService(int number, IThing thing) { }

        public static void RegisteringAttribute([RegistersOther] IThing thing) { }

        public static void AbstainingThenAnswering([Abstains] [RegistersOther] IThing thing) { }

        public static void Keyed([FromKeyedServices("other")] IThing thing) { }

        public static void UnregisteredWithInjectedValue([InjectValues("supplied")] NeedsAValue value) { }

        public static void TwoRegisteringAttributes([RegistersOther] IThing first, [RegistersOther] IThing second) { }
    }

    /// <summary>
    /// Stands in for [Mock]: registers a replacement during setup, then lets ordinary container
    /// resolution hand it back.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = true)]
    private class RegistersOtherAttribute : Attribute, ITestParameterValueProvider {
        public void SetupServiceCollection(
            ITestMethodContext testMethod, IServiceCollection serviceCollection, ParameterInfo parameter) =>
            serviceCollection.AddSingleton(parameter.ParameterType, new Other());

        public Task<object?> GetParameterValueAsync(
            ITestMethodContext testMethod, IServiceProvider serviceProvider, ParameterInfo parameter) =>
            Task.FromResult(serviceProvider.GetService(parameter.ParameterType));
    }

    /// <summary>
    /// Registers nothing and answers null, so the next provider on the parameter gets its turn.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = true)]
    private class AbstainsAttribute : Attribute, ITestParameterValueProvider {
        public void SetupServiceCollection(
            ITestMethodContext testMethod, IServiceCollection serviceCollection, ParameterInfo parameter) { }

        public Task<object?> GetParameterValueAsync(
            ITestMethodContext testMethod, IServiceProvider serviceProvider, ParameterInfo parameter) =>
            Task.FromResult<object?>(null);
    }
}
