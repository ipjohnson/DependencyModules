using System.Collections;
using System.Collections.Concurrent;
using DependencyModules.Runtime.Interfaces;

namespace DependencyModules.Runtime;

/// <summary>
/// A ready-made <see cref="IModuleEnvironment"/> for the common cases.
/// </summary>
/// <remarks>
/// Registration is decided while the service collection is being populated, so the environment has
/// to be known before the provider exists. That is why this is a plain object handed to
/// <c>AddModules</c> rather than something resolved from the container.
/// </remarks>
/// <example>
/// Values can be supplied inline, since this is a collection of them:
/// <code>
/// services.AddModules(
///     new ModuleEnvironment("Development") {
///         { "FEATURE_PROFILING", "on" },
///         { "REGION", "eu" }
///     },
///     new ApplicationModule());
/// </code>
/// </example>
public class ModuleEnvironment : IModuleEnvironment, IEnumerable<KeyValuePair<string, string?>> {
    private readonly Dictionary<string, string?> _values;
    private readonly bool _fallBackToEnvironmentVariables;

    // Null values are cached too — an unset optional variable is the case a default exists for, and
    // not caching it would leave the common path paying a process read every call.
    private readonly ConcurrentDictionary<string, string?> _processValues = new();

    /// <summary>
    /// Creates an environment with a fixed name and an optional set of values, falling back to
    /// environment variables for anything not supplied here.
    /// </summary>
    /// <param name="environmentName">The environment name conditions compare against.</param>
    /// <param name="values">Values reachable through <see cref="Value"/>; null means none.</param>
    public ModuleEnvironment(string environmentName, IReadOnlyDictionary<string, string?>? values = null)
        : this(true, environmentName, values) { }

    /// <summary>
    /// Creates an environment that reads only the values supplied here, with no fall back to
    /// environment variables.
    /// </summary>
    /// <remarks>
    /// The flag leads so that it is read before the values it governs, and so that turning it off
    /// cannot be mistaken for one more optional argument on the end.
    ///
    /// Pass false to pin an environment to exactly what is written at the call site. A test asserting
    /// which services a given environment registers wants this, since a variable set on the machine
    /// running it would otherwise reach a key the test never mentioned.
    /// </remarks>
    /// <param name="fallBackToEnvironmentVariables">
    /// False to read only <paramref name="values"/>. True behaves as the other constructor.
    /// </param>
    /// <param name="environmentName">The environment name conditions compare against.</param>
    /// <param name="values">Values reachable through <see cref="Value"/>; null means none.</param>
    public ModuleEnvironment(
        bool fallBackToEnvironmentVariables,
        string environmentName,
        IReadOnlyDictionary<string, string?>? values = null) {
        EnvironmentName = environmentName ?? throw new ArgumentNullException(nameof(environmentName));
        _fallBackToEnvironmentVariables = fallBackToEnvironmentVariables;

        // Copied rather than held by reference. Add writes to this dictionary, and writing into one
        // the caller still holds would be a side effect they did not ask for. A caller who supplied
        // a comparer picked it deliberately — most often OrdinalIgnoreCase, matching how Windows
        // treats variable names — so it is carried over instead of being reset to ordinal.
        _values = values switch {
            Dictionary<string, string?> dictionary =>
                new Dictionary<string, string?>(dictionary, dictionary.Comparer),
            not null => new Dictionary<string, string?>(values),
            null => new Dictionary<string, string?>()
        };
    }

    /// <inheritdoc />
    public string EnvironmentName { get; }

    /// <inheritdoc />
    /// <remarks>
    /// A key written here wins, including one written as null — saying a key has no value is how you
    /// hide an environment variable of the same name. Only a key that was never mentioned falls
    /// through to the process.
    ///
    /// A variable read from the process is cached for the life of this instance, misses included.
    /// This is injectable, so a service reading a value on every request would otherwise pay a
    /// process lookup and a fresh string allocation each time — and a miss is the common case, since
    /// an optional value that is not set is exactly what a default exists for. The cost of that is
    /// no longer seeing a variable changed mid-process, which nothing should be relying on; ask
    /// <see cref="CreateDefault"/> for a fresh view if you need one.
    /// </remarks>
    public string? Value(string name) {
        if (_values.TryGetValue(name, out var value)) {
            return value;
        }

        if (!_fallBackToEnvironmentVariables) {
            return null;
        }

        // Separate from _values rather than written back into it. That dictionary is what the caller
        // supplied, and GetEnumerator says so — folding process reads into it would have this
        // environment report values nobody gave it.
        return _processValues.GetOrAdd(name, static key => Environment.GetEnvironmentVariable(key));
    }

    /// <summary>
    /// Adds a value, replacing any already present for <paramref name="key"/>.
    /// </summary>
    /// <remarks>
    /// Present so that values can be written inline in a collection initializer, which is what this
    /// exists for. Replacing rather than throwing on a repeated key lets an initializer override a
    /// value seeded through the constructor, so the two can be combined.
    ///
    /// Nothing captures the environment, so a value added after <c>AddModules</c> has run is visible
    /// to whatever reads it next — but the registrations are already decided by then, and adding one
    /// late will not change them.
    /// </remarks>
    /// <param name="key">The key to write.</param>
    /// <param name="value">The value, which may be null.</param>
    public void Add(string key, string? value) => _values[key] = value;

    /// <summary>
    /// Enumerates the values supplied to this environment.
    /// </summary>
    /// <remarks>
    /// <see cref="IModuleEnvironment"/> answers lookups and nothing more, which leaves no way to
    /// combine two environments. Enumeration is on this class rather than on the interface so that
    /// a hand-written environment is not required to produce a list of everything it knows, which
    /// some cannot.
    /// </remarks>
    public IEnumerator<KeyValuePair<string, string?>> GetEnumerator() => _values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Reads the environment from the process: <c>ASPNETCORE_ENVIRONMENT</c>, then
    /// <c>DOTNET_ENVIRONMENT</c>, then <c>"Production"</c>, with values read as environment
    /// variables.
    /// </summary>
    /// <remarks>
    /// This is what a module gets when <c>AddModules</c> is called without one, so
    /// <c>[IfEnvironment("Development")]</c> works with nothing wired up beyond the variable every
    /// .NET developer already sets. The default of <c>"Production"</c> matches
    /// <c>IHostEnvironment</c>, and means a service gated on a non-production environment stays
    /// unregistered unless something says otherwise.
    ///
    /// A new instance each call, rather than one shared by the process. The instance caches what it
    /// reads, and a cache shared by every application in the process would let the first read of a
    /// variable fix it for all of them, with no way to opt out — the same reasoning that keeps
    /// <see cref="None"/> a type of its own.
    ///
    /// Because each call builds a fresh one, asking again is how you get a current view of the
    /// process. The instance <c>AddModules</c> registers is held for the application's lifetime, so
    /// a service injecting <see cref="IModuleEnvironment"/> reads through a warm cache.
    /// </remarks>
    public static IModuleEnvironment CreateDefault() => new ProcessModuleEnvironment();

    /// <summary>
    /// An environment with no name and no values, so every condition evaluates false.
    /// </summary>
    /// <remarks>
    /// Pass this to <c>AddModules</c> to state that this application has no environment, rather
    /// than leaving it unset and picking up <see cref="CreateDefault"/>.
    ///
    /// Its own type rather than an empty <see cref="ModuleEnvironment"/>: this instance is shared by
    /// every application in the process, and <see cref="Add"/> would let a cast reach in and give
    /// "no environment" some values.
    /// </remarks>
    public static IModuleEnvironment None { get; } = new EmptyModuleEnvironment();

    private sealed class ProcessModuleEnvironment : IModuleEnvironment {
        private readonly ConcurrentDictionary<string, string?> _values = new();

        // Not cached. It is read once per AddModules call rather than per service, and a fresh
        // instance is what CreateDefault hands out anyway.
        public string EnvironmentName =>
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ??
            Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ??
            "Production";

        public string? Value(string name) =>
            _values.GetOrAdd(name, static key => Environment.GetEnvironmentVariable(key));
    }

    private sealed class EmptyModuleEnvironment : IModuleEnvironment {
        public string EnvironmentName => "";

        public string? Value(string name) => null;
    }
}
