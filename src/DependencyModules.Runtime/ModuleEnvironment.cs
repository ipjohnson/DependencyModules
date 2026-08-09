using System.Collections;
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
    /// The variable is read on each call rather than captured, matching
    /// <see cref="Default"/>. Registration reads these once at startup, so nothing repeats the cost.
    /// </remarks>
    public string? Value(string name) =>
        _values.TryGetValue(name, out var value)
            ? value
            : _fallBackToEnvironmentVariables
                ? Environment.GetEnvironmentVariable(name)
                : null;

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
    /// Values are read on each call rather than captured, so a variable set after startup is still
    /// seen. Registration happens once, so the cost does not repeat.
    /// </remarks>
    public static IModuleEnvironment Default { get; } = new ProcessModuleEnvironment();

    /// <summary>
    /// An environment with no name and no values, so every condition evaluates false.
    /// </summary>
    /// <remarks>
    /// Pass this to <c>AddModules</c> to state that this application has no environment, rather
    /// than leaving it unset and picking up <see cref="Default"/>.
    ///
    /// Its own type rather than an empty <see cref="ModuleEnvironment"/>: this instance is shared by
    /// every application in the process, and <see cref="Add"/> would let a cast reach in and give
    /// "no environment" some values.
    /// </remarks>
    public static IModuleEnvironment None { get; } = new EmptyModuleEnvironment();

    private sealed class ProcessModuleEnvironment : IModuleEnvironment {
        public string EnvironmentName =>
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ??
            Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ??
            "Production";

        public string? Value(string name) => Environment.GetEnvironmentVariable(name);
    }

    private sealed class EmptyModuleEnvironment : IModuleEnvironment {
        public string EnvironmentName => "";

        public string? Value(string name) => null;
    }
}
