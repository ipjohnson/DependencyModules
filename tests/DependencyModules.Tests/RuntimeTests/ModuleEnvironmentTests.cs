using Microsoft.Extensions.DependencyInjection;
using DependencyModules.Runtime;
using DependencyModules.Runtime.Helpers;
using DependencyModules.Runtime.Interfaces;
using Xunit;

namespace DependencyModules.Tests.RuntimeTests;

public class ModuleEnvironmentTests {

    [Fact]
    public void ValuesComeBackByName() {
        var environment = new ModuleEnvironment(
            "Development",
            new Dictionary<string, string?> { ["A"] = "1", ["Empty"] = "" });

        Assert.Equal("Development", environment.EnvironmentName);
        Assert.Equal("1", environment.Value("A"));
        Assert.Equal("", environment.Value("Empty"));
        Assert.Null(environment.Value("Missing"));
    }

    [Fact]
    public void ValuesAreOptional() {
        var environment = new ModuleEnvironment("Production");

        Assert.Null(environment.Value("Anything"));
    }

    [Fact]
    public void ValuesCanBeWrittenInAnInitializer() {
        var environment = new ModuleEnvironment("Development") {
            { "A", "1" },
            { "Null", null }
        };

        Assert.Equal("1", environment.Value("A"));
        Assert.Null(environment.Value("Null"));
        Assert.Null(environment.Value("Missing"));
    }

    /// <summary>
    /// So a fixed set can be seeded and then adjusted, rather than the two forms being exclusive.
    /// </summary>
    [Fact]
    public void AnInitializerOverridesAValueFromTheConstructor() {
        var environment = new ModuleEnvironment(
            "Development",
            new Dictionary<string, string?> { ["Seed"] = "original", ["Kept"] = "kept" }) {
            { "Seed", "replaced" }
        };

        Assert.Equal("replaced", environment.Value("Seed"));
        Assert.Equal("kept", environment.Value("Kept"));
    }

    /// <summary>
    /// The values are copied, so the dictionary the caller still holds is not written to.
    /// </summary>
    [Fact]
    public void AddDoesNotWriteToTheCallersDictionary() {
        var values = new Dictionary<string, string?> { ["A"] = "1" };
        var environment = new ModuleEnvironment("Development", values) { { "B", "2" } };

        Assert.Equal("2", environment.Value("B"));
        Assert.DoesNotContain("B", values.Keys);
    }

    /// <summary>
    /// A comparer is a deliberate choice — most often to match how Windows treats variable names —
    /// so copying the values must not quietly reset it to ordinal.
    /// </summary>
    [Fact]
    public void ACallersComparerSurvivesTheCopy() {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) {
            ["Key"] = "value"
        };

        var environment = new ModuleEnvironment("Development", values);

        Assert.Equal("value", environment.Value("KEY"));
    }

    [Fact]
    public void ValuesEnumerate() {
        var environment = new ModuleEnvironment("Development") {
            { "A", "1" },
            { "B", "2" }
        };

        Assert.Equal(
            new Dictionary<string, string?> { ["A"] = "1", ["B"] = "2" },
            environment.ToDictionary(pair => pair.Key, pair => pair.Value));
    }

    /// <summary>
    /// Shared by every application in the process, so it cannot be one of the mutable ones.
    /// </summary>
    [Fact]
    public void NoneCannotBeGivenValues() {
        Assert.IsNotType<ModuleEnvironment>(ModuleEnvironment.None);
    }

    /// <summary>
    /// Uniquely named so nothing else in the suite, and nothing on the machine, can be looking at it.
    /// </summary>
    private static string UniqueKey() => "DM_TEST_" + Guid.NewGuid().ToString("N");

    [Fact]
    public void AKeyNotSuppliedFallsBackToAnEnvironmentVariable() {
        var key = UniqueKey();

        try {
            // Set before the environment is built. An instance caches what it reads, so reading the
            // key first and setting the variable afterwards would be testing the cache instead of
            // the fallback — see FallBackToTheProcessIsCachedPerInstance for that.
            Environment.SetEnvironmentVariable(key, "from-process");

            var environment = new ModuleEnvironment("Development") { { "Supplied", "value" } };

            Assert.Equal("from-process", environment.Value(key));
            Assert.Equal("value", environment.Value("Supplied"));
            Assert.Null(environment.Value(UniqueKey()));
        } finally {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    [Fact]
    public void ASuppliedValueWinsOverAnEnvironmentVariable() {
        var key = UniqueKey();

        try {
            Environment.SetEnvironmentVariable(key, "from-process");

            var environment = new ModuleEnvironment("Development") { { key, "supplied" } };

            Assert.Equal("supplied", environment.Value(key));
        } finally {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    /// <summary>
    /// Saying a key has no value is how an environment variable of the same name is hidden.
    /// </summary>
    [Fact]
    public void ASuppliedNullHidesAnEnvironmentVariable() {
        var key = UniqueKey();

        try {
            Environment.SetEnvironmentVariable(key, "from-process");

            var environment = new ModuleEnvironment("Development") { { key, null } };

            Assert.Null(environment.Value(key));
            Assert.False(EnvironmentConditions.HasValue(environment, key));
        } finally {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    [Fact]
    public void FallBackCanBeTurnedOff() {
        var key = UniqueKey();

        try {
            Environment.SetEnvironmentVariable(key, "from-process");

            var environment = new ModuleEnvironment(false, "Development") { { "Supplied", "value" } };

            Assert.Null(environment.Value(key));
            Assert.Equal("value", environment.Value("Supplied"));
        } finally {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    /// <summary>
    /// The values still travel with it, so turning fall back off does not mean giving up the
    /// constructor that takes a dictionary.
    /// </summary>
    [Fact]
    public void FallBackCanBeTurnedOffWithValuesSuppliedUpFront() {
        var environment = new ModuleEnvironment(
            false,
            "Development",
            new Dictionary<string, string?> { ["A"] = "1" });

        Assert.Equal("Development", environment.EnvironmentName);
        Assert.Equal("1", environment.Value("A"));
    }

    /// <summary>
    /// An empty name and no values, whatever the machine running this has set.
    /// </summary>
    [Fact]
    public void NoneDoesNotFallBack() {
        var key = UniqueKey();

        try {
            Environment.SetEnvironmentVariable(key, "from-process");

            Assert.Null(ModuleEnvironment.None.Value(key));
        } finally {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    [Fact]
    public void NoneHasNoNameAndNoValues() {
        Assert.Equal("", ModuleEnvironment.None.EnvironmentName);
        Assert.Null(ModuleEnvironment.None.Value("Anything"));
    }

    /// <summary>
    /// A fresh default reads the process as it is now, which is what asking again is for.
    /// </summary>
    [Fact]
    public void DefaultReadsValuesFromTheProcess() {
        // Uniquely named so nothing else in the suite can be looking at it.
        var key = "DM_TEST_" + Guid.NewGuid().ToString("N");

        Assert.Null(ModuleEnvironment.CreateDefault().Value(key));

        try {
            Environment.SetEnvironmentVariable(key, "set-after-startup");

            Assert.Equal("set-after-startup", ModuleEnvironment.CreateDefault().Value(key));
        } finally {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    /// <summary>
    /// One instance caches what it read, misses included.
    /// </summary>
    /// <remarks>
    /// The instance AddModules registers is held for the application's lifetime, so a service
    /// injecting it would otherwise pay a process lookup and a string allocation on every read. A
    /// miss is the case worth caching: an optional variable that is not set is exactly what a
    /// default exists for, and re-reading it each call would leave the common path uncached.
    /// </remarks>
    [Fact]
    public void AHeldDefaultCachesWhatItRead() {
        var key = "DM_TEST_" + Guid.NewGuid().ToString("N");

        var held = ModuleEnvironment.CreateDefault();

        Assert.Null(held.Value(key));

        try {
            Environment.SetEnvironmentVariable(key, "set-after-the-read");

            // The miss was cached, so this instance keeps answering with what it saw.
            Assert.Null(held.Value(key));

            // Asking for a new one is how a current view is obtained.
            Assert.Equal("set-after-the-read", ModuleEnvironment.CreateDefault().Value(key));
        } finally {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    /// <summary>
    /// The fallback on a named environment caches the same way.
    /// </summary>
    [Fact]
    public void FallBackToTheProcessIsCachedPerInstance() {
        var key = "DM_TEST_" + Guid.NewGuid().ToString("N");

        try {
            Environment.SetEnvironmentVariable(key, "first");

            var environment = new ModuleEnvironment("Development");

            Assert.Equal("first", environment.Value(key));

            Environment.SetEnvironmentVariable(key, "second");

            Assert.Equal("first", environment.Value(key));
            Assert.Equal("second", new ModuleEnvironment("Development").Value(key));
        } finally {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    /// <summary>
    /// Caching the process must not make the environment report values nobody supplied.
    /// </summary>
    [Fact]
    public void CachedProcessValuesDoNotAppearInEnumeration() {
        var key = "DM_TEST_" + Guid.NewGuid().ToString("N");

        try {
            Environment.SetEnvironmentVariable(key, "from-process");

            var environment = new ModuleEnvironment("Development") { { "Supplied", "yes" } };

            Assert.Equal("from-process", environment.Value(key));

            Assert.Equal(
                new Dictionary<string, string?> { ["Supplied"] = "yes" },
                environment.ToDictionary(pair => pair.Key, pair => pair.Value));
        } finally {
            Environment.SetEnvironmentVariable(key, null);
        }
    }
}

/// <summary>
/// The name fallback, which can only be exercised by moving process-wide variables.
/// </summary>
/// <remarks>
/// In its own collection so these do not run alongside each other. Nothing else in the suite reads
/// these two variables — the generator tests that involve a default environment compare against
/// <see cref="ModuleEnvironment.Default"/> rather than against a literal name, for this reason.
/// </remarks>
[Collection("ProcessEnvironment")]
public class ModuleEnvironmentDefaultNameTests {

    private const string AspNetCore = "ASPNETCORE_ENVIRONMENT";
    private const string DotNet = "DOTNET_ENVIRONMENT";

    [Theory]
    [InlineData(null, null, "Production")]
    [InlineData("Development", null, "Development")]
    [InlineData(null, "Staging", "Staging")]
    // ASPNETCORE_ENVIRONMENT wins, matching how a web host resolves it.
    [InlineData("Development", "Staging", "Development")]
    public void DefaultResolvesTheEnvironmentName(string? aspNetCore, string? dotNet, string expected) {
        var originalAspNetCore = Environment.GetEnvironmentVariable(AspNetCore);
        var originalDotNet = Environment.GetEnvironmentVariable(DotNet);

        try {
            Environment.SetEnvironmentVariable(AspNetCore, aspNetCore);
            Environment.SetEnvironmentVariable(DotNet, dotNet);

            Assert.Equal(expected, ModuleEnvironment.CreateDefault().EnvironmentName);
        } finally {
            Environment.SetEnvironmentVariable(AspNetCore, originalAspNetCore);
            Environment.SetEnvironmentVariable(DotNet, originalDotNet);
        }
    }
}

/// <summary>
/// How the one environment for an AddModules call is arrived at.
/// </summary>
/// <remarks>
/// One environment, not several. It decides what gets registered, which is a single question with a
/// single answer; several would need a rule for which one the conditions read, and that rule would
/// fall out of module ordering rather than out of anything the developer wrote.
/// </remarks>
public class EnvironmentDiscoveryTests {

    private class StubEnvironment(string name) : IModuleEnvironment {
        public string EnvironmentName => name;
        public string? Value(string valueName) => null;
    }

    private class ProbeModule : IDependencyModule, IEnvironmentServiceCollectionConfiguration {
        public IModuleEnvironment? Seen { get; private set; }

        public void PopulateServiceCollection(IServiceCollection serviceCollection) { }

        public void ConfigureServices(IServiceCollection services, IModuleEnvironment environment) =>
            Seen = environment;
    }

    [Fact]
    public void AnInstanceRegisteredBeforeAddModulesIsUsed() {
        var environment = new StubEnvironment("Staging");
        var module = new ProbeModule();

        var collection = new ServiceCollection();
        collection.AddSingleton<IModuleEnvironment>(environment);
        collection.AddModules(module);

        Assert.Same(environment, module.Seen);
        Assert.Single(collection, d => d.ServiceType == typeof(IModuleEnvironment));
    }

    [Fact]
    public void AnEnvironmentPassedToAddModulesReplacesOneAlreadyRegistered() {
        var registered = new StubEnvironment("Staging");
        var passed = new StubEnvironment("Development");
        var module = new ProbeModule();

        var collection = new ServiceCollection();
        collection.AddSingleton<IModuleEnvironment>(registered);
        collection.AddModules(passed, module);

        Assert.Same(passed, module.Seen);

        // Replaced rather than joined, so what resolves is what decided the registrations.
        var descriptor = Assert.Single(collection, d => d.ServiceType == typeof(IModuleEnvironment));
        Assert.Same(passed, descriptor.ImplementationInstance);
    }

    /// <summary>
    /// Registered by type it cannot be constructed — there is no provider yet — so it is refused
    /// rather than ignored in favour of the process default.
    /// </summary>
    [Fact]
    public void AnEnvironmentRegisteredByTypeIsRefused() {
        var collection = new ServiceCollection();
        collection.AddSingleton<IModuleEnvironment, StubByType>();

        var exception = Assert.Throws<InvalidOperationException>(
            () => collection.AddModules(new ProbeModule()));

        Assert.Contains("singleton instance", exception.Message);
    }

    [Fact]
    public void AnEnvironmentRegisteredByFactoryIsRefused() {
        var collection = new ServiceCollection();
        collection.AddSingleton<IModuleEnvironment>(_ => new StubEnvironment("Development"));

        Assert.Throws<InvalidOperationException>(() => collection.AddModules(new ProbeModule()));
    }

    private class StubByType : IModuleEnvironment {
        public string EnvironmentName => "Development";
        public string? Value(string valueName) => null;
    }
}

public class EnvironmentConditionsTests {

    /// <summary>
    /// Pinned to the values written here. These assert what a condition does with a given set of
    /// values, so a variable set on the machine running them must not reach a key they never name.
    /// </summary>
    private static IModuleEnvironment Env(string name, params (string Key, string? Value)[] values) =>
        new ModuleEnvironment(false, name, values.ToDictionary(v => v.Key, v => v.Value));

    [Theory]
    [InlineData("Development", true)]
    [InlineData("development", true)]
    [InlineData("DEVELOPMENT", true)]
    [InlineData("Production", false)]
    public void NameIsIgnoresCase(string environmentName, bool expected) =>
        Assert.Equal(expected, EnvironmentConditions.NameIs(Env(environmentName), "Development"));

    [Fact]
    public void NameIsAcceptsAnyOfSeveral() {
        Assert.True(EnvironmentConditions.NameIs(Env("Staging"), "Development", "Staging"));
        Assert.False(EnvironmentConditions.NameIs(Env("Production"), "Development", "Staging"));
    }

    [Fact]
    public void NameIsWithNoNamesMatchesNothing() =>
        Assert.False(EnvironmentConditions.NameIs(Env("Development")));

    [Fact]
    public void HasValueIsPresenceNotTruthiness() {
        Assert.True(EnvironmentConditions.HasValue(Env("Any", ("K", "v")), "K"));
        Assert.True(EnvironmentConditions.HasValue(Env("Any", ("K", "")), "K"));
        Assert.False(EnvironmentConditions.HasValue(Env("Any"), "K"));
    }

    [Fact]
    public void ValueIsComparesOrdinally() {
        Assert.True(EnvironmentConditions.ValueIs(Env("Any", ("K", "on")), "K", "on"));
        Assert.False(EnvironmentConditions.ValueIs(Env("Any", ("K", "On")), "K", "on"));
        Assert.False(EnvironmentConditions.ValueIs(Env("Any"), "K", "on"));
    }

    /// <summary>
    /// Generated code is handed a non-null environment, but these are public and a hand-written
    /// module can reach them. Refusing to match beats throwing out of a registration.
    /// </summary>
    [Fact]
    public void ANullEnvironmentMatchesNothing() {
        Assert.False(EnvironmentConditions.NameIs(null!, "Development"));
        Assert.False(EnvironmentConditions.HasValue(null!, "K"));
        Assert.False(EnvironmentConditions.ValueIs(null!, "K", "v"));
    }
}
