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

    /// <summary>A second real implementation, for the displacement case.</summary>
    private class Other2 : IThing;

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

    /// <summary>
    /// A [Mock] on a keyed parameter replaces the <i>keyed</i> registration. It used to register the
    /// double unkeyed, leaving the keyed registration — the one a consumer injects — untouched, so
    /// the service under test kept the real implementation while the test held a double it believed
    /// was wired in.
    /// </summary>
    [Fact]
    public async Task KeyedMockReplacesTheKeyedRegistration() {
        var (resolver, provider) = Build(
            nameof(Samples.KeyedMock),
            services => services.AddKeyedSingleton<IThing, Thing>("primary"));

        var arguments = await resolver.ResolveArgumentsAsync(provider, []);

        Assert.IsType<Other>(Assert.Single(arguments));
        Assert.IsType<Other>(provider.GetRequiredKeyedService<IThing>("primary"));
    }

    /// <summary>
    /// And it does not spill into the unkeyed slot, where nothing asked for it.
    /// </summary>
    [Fact]
    public void KeyedMockRegistersNothingUnkeyed() {
        var (_, provider) = Build(
            nameof(Samples.KeyedMock),
            services => services.AddKeyedSingleton<IThing, Thing>("primary"));

        Assert.Null(provider.GetService<IThing>());
    }

    /// <summary>
    /// A key the mock did not name is left alone, so mocking one keyed implementation leaves its
    /// siblings real.
    /// </summary>
    [Fact]
    public void KeyedMockLeavesOtherKeysAlone() {
        var (_, provider) = Build(
            nameof(Samples.KeyedMock),
            services => {
                services.AddKeyedSingleton<IThing, Thing>("primary");
                services.AddKeyedSingleton<IThing, Thing>("secondary");
            });

        Assert.IsType<Thing>(provider.GetRequiredKeyedService<IThing>("secondary"));
    }

    /// <summary>Control: an unkeyed mock still replaces the unkeyed registration.</summary>
    [Fact]
    public async Task UnkeyedMockReplacesTheUnkeyedRegistration() {
        var arguments = await Resolve(
            nameof(Samples.UnkeyedMock), services => services.AddSingleton<IThing, Thing>());

        Assert.IsType<Other>(Assert.Single(arguments));
    }

    /// <summary>
    /// A [Mock] on a parameter overrides a registration made from a wider scope — a [TestExport] on
    /// the method, the class or the assembly. A parameter attribute names one argument, which is the
    /// narrowest thing a test can say, so it is the one that decides.
    ///
    /// The hosts arrange that by running this pass last, after the setup attributes, and this is the
    /// half of it the resolver owns: whatever was registered before it, the double registered here
    /// is the one the container ends up with.
    /// </summary>
    [Fact]
    public async Task AMockOnAParameter_BeatsARegistrationMadeBeforeIt() {
        var services = new ServiceCollection();
        var resolver = new TestParameterResolver(ContextFor(nameof(Samples.UnkeyedMock)));

        // What a [TestExport] from any wider scope leaves behind, since the setup-attribute pass
        // runs first.
        services.AddSingleton<IThing, Thing>();
        services.AddSingleton<IThing, Other2>();

        resolver.SetupServiceCollection(services);

        var arguments = await resolver.ResolveArgumentsAsync(services.BuildServiceProvider(), []);

        Assert.IsType<Other>(Assert.Single(arguments));
    }

    /// <summary>
    /// And the service under test sees the same double, which is why [Mock] registers at all rather
    /// than merely supplying an argument.
    /// </summary>
    [Fact]
    public void AMockOnAParameter_IsWhatTheContainerHandsOut() {
        var services = new ServiceCollection();
        var resolver = new TestParameterResolver(ContextFor(nameof(Samples.UnkeyedMock)));

        services.AddSingleton<IThing, Other2>();
        resolver.SetupServiceCollection(services);

        Assert.IsType<Other>(services.BuildServiceProvider().GetRequiredService<IThing>());
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
    [StubMockSupport]
    private static class Samples {
        public static void OneService(IThing thing) { }

        public static void KeyedMock([Mock] [FromKeyedServices("primary")] IThing thing) { }

        public static void UnkeyedMock([Mock] IThing thing) { }

        public static void WantsTheProvider(IServiceProvider provider) { }

        public static void DataThenService(int number, IThing thing) { }

        public static void RegisteringAttribute([RegistersOther] IThing thing) { }

        public static void AbstainingThenAnswering([Abstains] [RegistersOther] IThing thing) { }

        public static void Keyed([FromKeyedServices("other")] IThing thing) { }

        public static void UnregisteredWithInjectedValue([InjectValues("supplied")] NeedsAValue value) { }

        public static void TwoRegisteringAttributes([RegistersOther] IThing first, [RegistersOther] IThing second) { }
    }

    /// <summary>
    /// Stands in for a mocking package, so the real [Mock] can be driven without one. What the
    /// double actually is does not matter here; where it gets registered does.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    private class StubMockSupportAttribute : Attribute, IMockSupportAttribute {
        public object ProvideMock(Type type) => new Other();
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
