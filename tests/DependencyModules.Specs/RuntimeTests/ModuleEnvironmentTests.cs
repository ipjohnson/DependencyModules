using DependencyModules.Runtime;
using DependencyModules.Runtime.Helpers;
using DependencyModules.Runtime.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Specs.RuntimeTests;

public partial class ModuleEnvironmentTests
{
    [VerifyTest]
    public void ValuesComeBackByName()
    {
        var environment = new ModuleEnvironment(
            "Development",
            new Dictionary<string, string?> { ["A"] = "1", ["Empty"] = "" }
        );

        Verify(environment.EnvironmentName == "Development");
        Verify(environment.Value("A") == "1");
        Verify(environment.Value("Empty") == "");
        Verify(environment.Value("Missing") is null);
    }

    [VerifyTest]
    public void ValuesAreOptional()
    {
        var environment = new ModuleEnvironment("Production");

        Verify(environment.Value("Anything") is null);
    }

    [VerifyTest]
    public void ValuesCanBeWrittenInAnInitializer()
    {
        var environment = new ModuleEnvironment("Development") { { "A", "1" }, { "Null", null } };

        Verify(environment.Value("A") == "1");
        Verify(environment.Value("Null") is null);
        Verify(environment.Value("Missing") is null);
    }

    /// <summary>
    /// So a fixed set can be seeded and then adjusted, rather than the two forms being exclusive.
    /// </summary>
    [VerifyTest]
    public void AnInitializerOverridesAValueFromTheConstructor()
    {
        var environment = new ModuleEnvironment(
            "Development",
            new Dictionary<string, string?> { ["Seed"] = "original", ["Kept"] = "kept" }
        )
        {
            { "Seed", "replaced" },
        };

        Verify(environment.Value("Seed") == "replaced");
        Verify(environment.Value("Kept") == "kept");
    }

    /// <summary>
    /// The values are copied, so the dictionary the caller still holds is not written to.
    /// </summary>
    [VerifyTest]
    public void AddDoesNotWriteToTheCallersDictionary()
    {
        var values = new Dictionary<string, string?> { ["A"] = "1" };
        var environment = new ModuleEnvironment("Development", values) { { "B", "2" } };

        Verify(environment.Value("B") == "2");
        Verify(!values.Keys.Contains("B"));
    }

    /// <summary>
    /// A comparer is a deliberate choice — most often to match how Windows treats variable names —
    /// so copying the values must not quietly reset it to ordinal.
    /// </summary>
    [VerifyTest]
    public void ACallersComparerSurvivesTheCopy()
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Key"] = "value",
        };

        var environment = new ModuleEnvironment("Development", values);

        Verify(environment.Value("KEY") == "value");
    }

    [VerifyTest]
    public void ValuesEnumerate()
    {
        var environment = new ModuleEnvironment("Development") { { "A", "1" }, { "B", "2" } };

        var actual = environment.ToDictionary(pair => pair.Key, pair => pair.Value);

        Verify(actual.Count == 2);
        Verify(actual["A"] == "1");
        Verify(actual["B"] == "2");
    }

    /// <summary>
    /// Shared by every application in the process, so it cannot be one of the mutable ones.
    /// </summary>
    [VerifyTest]
    public void NoneCannotBeGivenValues()
    {
        Verify(ModuleEnvironment.None.GetType() != typeof(ModuleEnvironment));
    }

    /// <summary>
    /// Uniquely named so nothing else in the suite, and nothing on the machine, can be looking at it.
    /// </summary>
    private static string UniqueKey() => "DM_TEST_" + Guid.NewGuid().ToString("N");

    [VerifyTest]
    public void AKeyNotSuppliedFallsBackToAnEnvironmentVariable()
    {
        var key = UniqueKey();

        try
        {
            // Set before the environment is built. An instance caches what it reads, so reading the
            // key first and setting the variable afterwards would be testing the cache instead of
            // the fallback — see FallBackToTheProcessIsCachedPerInstance for that.
            Environment.SetEnvironmentVariable(key, "from-process");

            var environment = new ModuleEnvironment("Development") { { "Supplied", "value" } };

            Verify(environment.Value(key) == "from-process");
            Verify(environment.Value("Supplied") == "value");
            Verify(environment.Value(UniqueKey()) is null);
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    [VerifyTest]
    public void ASuppliedValueWinsOverAnEnvironmentVariable()
    {
        var key = UniqueKey();

        try
        {
            Environment.SetEnvironmentVariable(key, "from-process");

            var environment = new ModuleEnvironment("Development") { { key, "supplied" } };

            Verify(environment.Value(key) == "supplied");
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    /// <summary>
    /// Saying a key has no value is how an environment variable of the same name is hidden.
    /// </summary>
    [VerifyTest]
    public void ASuppliedNullHidesAnEnvironmentVariable()
    {
        var key = UniqueKey();

        try
        {
            Environment.SetEnvironmentVariable(key, "from-process");

            var environment = new ModuleEnvironment("Development") { { key, null } };

            Verify(environment.Value(key) is null);
            Verify(!EnvironmentConditions.HasValue(environment, key));
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    [VerifyTest]
    public void FallBackCanBeTurnedOff()
    {
        var key = UniqueKey();

        try
        {
            Environment.SetEnvironmentVariable(key, "from-process");

            var environment = new ModuleEnvironment(false, "Development")
            {
                { "Supplied", "value" },
            };

            Verify(environment.Value(key) is null);
            Verify(environment.Value("Supplied") == "value");
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    /// <summary>
    /// The values still travel with it, so turning fall back off does not mean giving up the
    /// constructor that takes a dictionary.
    /// </summary>
    [VerifyTest]
    public void FallBackCanBeTurnedOffWithValuesSuppliedUpFront()
    {
        var environment = new ModuleEnvironment(
            false,
            "Development",
            new Dictionary<string, string?> { ["A"] = "1" }
        );

        Verify(environment.EnvironmentName == "Development");
        Verify(environment.Value("A") == "1");
    }

    /// <summary>
    /// An empty name and no values, whatever the machine running this has set.
    /// </summary>
    [VerifyTest]
    public void NoneDoesNotFallBack()
    {
        var key = UniqueKey();

        try
        {
            Environment.SetEnvironmentVariable(key, "from-process");

            Verify(ModuleEnvironment.None.Value(key) is null);
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    [VerifyTest]
    public void NoneHasNoNameAndNoValues()
    {
        Verify(ModuleEnvironment.None.EnvironmentName == "");
        Verify(ModuleEnvironment.None.Value("Anything") is null);
    }

    /// <summary>
    /// A fresh default reads the process as it is now, which is what asking again is for.
    /// </summary>
    [VerifyTest]
    public void DefaultReadsValuesFromTheProcess()
    {
        // Uniquely named so nothing else in the suite can be looking at it.
        var key = "DM_TEST_" + Guid.NewGuid().ToString("N");

        Verify(ModuleEnvironment.CreateDefault().Value(key) is null);

        try
        {
            Environment.SetEnvironmentVariable(key, "set-after-startup");

            Verify(ModuleEnvironment.CreateDefault().Value(key) == "set-after-startup");
        }
        finally
        {
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
    [VerifyTest]
    public void AHeldDefaultCachesWhatItRead()
    {
        var key = "DM_TEST_" + Guid.NewGuid().ToString("N");

        var held = ModuleEnvironment.CreateDefault();

        Verify(held.Value(key) is null);

        try
        {
            Environment.SetEnvironmentVariable(key, "set-after-the-read");

            // The miss was cached, so this instance keeps answering with what it saw.
            Verify(held.Value(key) is null);

            // Asking for a new one is how a current view is obtained.
            Verify(ModuleEnvironment.CreateDefault().Value(key) == "set-after-the-read");
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    /// <summary>
    /// The fallback on a named environment caches the same way.
    /// </summary>
    [VerifyTest]
    public void FallBackToTheProcessIsCachedPerInstance()
    {
        var key = "DM_TEST_" + Guid.NewGuid().ToString("N");

        try
        {
            Environment.SetEnvironmentVariable(key, "first");

            var environment = new ModuleEnvironment("Development");

            Verify(environment.Value(key) == "first");

            Environment.SetEnvironmentVariable(key, "second");

            Verify(environment.Value(key) == "first");
            Verify(new ModuleEnvironment("Development").Value(key) == "second");
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    /// <summary>
    /// Caching the process must not make the environment report values nobody supplied.
    /// </summary>
    [VerifyTest]
    public void CachedProcessValuesDoNotAppearInEnumeration()
    {
        var key = "DM_TEST_" + Guid.NewGuid().ToString("N");

        try
        {
            Environment.SetEnvironmentVariable(key, "from-process");

            var environment = new ModuleEnvironment("Development") { { "Supplied", "yes" } };

            Verify(environment.Value(key) == "from-process");

            var actual = environment.ToDictionary(pair => pair.Key, pair => pair.Value);

            Verify(actual.Count == 1);
            Verify(actual["Supplied"] == "yes");
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }
}

/// <summary>
/// The name fallback, which can only be exercised by moving process-wide variables.
/// </summary>
/// <remarks>
/// Holds the ProcessEnvironment resource so these do not run alongside each other. Nothing else in
/// the suite reads these two variables — the generator tests that involve a default environment
/// compare against <see cref="ModuleEnvironment.Default"/> rather than against a literal name, for
/// this reason.
/// </remarks>
[Resource("ProcessEnvironment")]
public partial class ModuleEnvironmentDefaultNameTests
{
    private const string AspNetCore = "ASPNETCORE_ENVIRONMENT";
    private const string DotNet = "DOTNET_ENVIRONMENT";

    [VerifyTest]
    [Row(null, null, "Production")]
    [Row("Development", null, "Development")]
    [Row(null, "Staging", "Staging")]
    // ASPNETCORE_ENVIRONMENT wins, matching how a web host resolves it.
    [Row("Development", "Staging", "Development")]
    public void DefaultResolvesTheEnvironmentName(
        string? aspNetCore,
        string? dotNet,
        string expected
    )
    {
        var originalAspNetCore = Environment.GetEnvironmentVariable(AspNetCore);
        var originalDotNet = Environment.GetEnvironmentVariable(DotNet);

        try
        {
            Environment.SetEnvironmentVariable(AspNetCore, aspNetCore);
            Environment.SetEnvironmentVariable(DotNet, dotNet);

            Verify(ModuleEnvironment.CreateDefault().EnvironmentName == expected);
        }
        finally
        {
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
public partial class EnvironmentDiscoveryTests
{
    private class StubEnvironment(string name) : IModuleEnvironment
    {
        public string EnvironmentName => name;

        public string? Value(string valueName) => null;
    }

    private class ProbeModule : IDependencyModule, IEnvironmentServiceCollectionConfiguration
    {
        public IModuleEnvironment? Seen { get; private set; }

        public void PopulateServiceCollection(IServiceCollection serviceCollection) { }

        public void ConfigureServices(
            IServiceCollection services,
            IModuleEnvironment environment
        ) => Seen = environment;
    }

    [VerifyTest]
    public void AnInstanceRegisteredBeforeAddModulesIsUsed()
    {
        var environment = new StubEnvironment("Staging");
        var module = new ProbeModule();

        var collection = new ServiceCollection();
        collection.AddSingleton<IModuleEnvironment>(environment);
        collection.AddModules(module);

        Verify(ReferenceEquals(module.Seen, environment));
        Single(collection, d => d.ServiceType == typeof(IModuleEnvironment));
    }

    [VerifyTest]
    public void AnEnvironmentPassedToAddModulesReplacesOneAlreadyRegistered()
    {
        var registered = new StubEnvironment("Staging");
        var passed = new StubEnvironment("Development");
        var module = new ProbeModule();

        var collection = new ServiceCollection();
        collection.AddSingleton<IModuleEnvironment>(registered);
        collection.AddModules(passed, module);

        Verify(ReferenceEquals(module.Seen, passed));

        // Replaced rather than joined, so what resolves is what decided the registrations.
        var descriptor = Single(collection, d => d.ServiceType == typeof(IModuleEnvironment));
        Verify(ReferenceEquals(descriptor.ImplementationInstance, passed));
    }

    /// <summary>
    /// Registered by type it cannot be constructed — there is no provider yet — so it is refused
    /// rather than ignored in favour of the process default.
    /// </summary>
    [VerifyTest]
    public void AnEnvironmentRegisteredByTypeIsRefused()
    {
        var collection = new ServiceCollection();
        collection.AddSingleton<IModuleEnvironment, StubByType>();

        var exception = ThrowsExactly<InvalidOperationException>(() =>
            collection.AddModules(new ProbeModule())
        );

        Verify(exception.Message.Contains("singleton instance"));
    }

    [VerifyTest]
    public void AnEnvironmentRegisteredByFactoryIsRefused()
    {
        var collection = new ServiceCollection();
        collection.AddSingleton<IModuleEnvironment>(_ => new StubEnvironment("Development"));

        ThrowsExactly<InvalidOperationException>(() => collection.AddModules(new ProbeModule()));
    }

    private class StubByType : IModuleEnvironment
    {
        public string EnvironmentName => "Development";

        public string? Value(string valueName) => null;
    }
}

public partial class EnvironmentConditionsTests
{
    /// <summary>
    /// Pinned to the values written here. These check what a condition does with a given set of
    /// values, so a variable set on the machine running them must not reach a key they never name.
    /// </summary>
    private static IModuleEnvironment Env(
        string name,
        params (string Key, string? Value)[] values
    ) => new ModuleEnvironment(false, name, values.ToDictionary(v => v.Key, v => v.Value));

    [VerifyTest]
    [Row("Development", true)]
    [Row("development", true)]
    [Row("DEVELOPMENT", true)]
    [Row("Production", false)]
    public void NameIsIgnoresCase(string environmentName, bool expected) =>
        Verify(EnvironmentConditions.NameIs(Env(environmentName), "Development") == expected);

    [VerifyTest]
    public void NameIsAcceptsAnyOfSeveral()
    {
        Verify(EnvironmentConditions.NameIs(Env("Staging"), "Development", "Staging"));
        Verify(!EnvironmentConditions.NameIs(Env("Production"), "Development", "Staging"));
    }

    [VerifyTest]
    public void NameIsWithNoNamesMatchesNothing() =>
        Verify(!EnvironmentConditions.NameIs(Env("Development")));

    [VerifyTest]
    public void HasValueIsPresenceNotTruthiness()
    {
        Verify(EnvironmentConditions.HasValue(Env("Any", ("K", "v")), "K"));
        Verify(EnvironmentConditions.HasValue(Env("Any", ("K", "")), "K"));
        Verify(!EnvironmentConditions.HasValue(Env("Any"), "K"));
    }

    [VerifyTest]
    public void ValueIsComparesOrdinally()
    {
        Verify(EnvironmentConditions.ValueIs(Env("Any", ("K", "on")), "K", "on"));
        Verify(!EnvironmentConditions.ValueIs(Env("Any", ("K", "On")), "K", "on"));
        Verify(!EnvironmentConditions.ValueIs(Env("Any"), "K", "on"));
    }

    /// <summary>
    /// Generated code is handed a non-null environment, but these are public and a hand-written
    /// module can reach them. Refusing to match beats throwing out of a registration.
    /// </summary>
    [VerifyTest]
    public void ANullEnvironmentMatchesNothing()
    {
        Verify(!EnvironmentConditions.NameIs(null!, "Development"));
        Verify(!EnvironmentConditions.HasValue(null!, "K"));
        Verify(!EnvironmentConditions.ValueIs(null!, "K", "v"));
    }
}
