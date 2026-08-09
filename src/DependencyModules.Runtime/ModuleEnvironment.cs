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
public class ModuleEnvironment : IModuleEnvironment {
    // Declared before None, which constructs an instance during static initialization and would
    // otherwise capture this field before it is assigned. Static field initializers run in
    // declaration order, so the order here is load-bearing.
    private static readonly IReadOnlyDictionary<string, string?> EmptyValues =
        new Dictionary<string, string?>(0);

    private readonly IReadOnlyDictionary<string, string?> _values;

    /// <summary>
    /// Creates an environment with a fixed name and an optional set of values.
    /// </summary>
    /// <param name="environmentName">The environment name conditions compare against.</param>
    /// <param name="values">Values reachable through <see cref="Value"/>; null means none.</param>
    public ModuleEnvironment(string environmentName, IReadOnlyDictionary<string, string?>? values = null) {
        EnvironmentName = environmentName ?? throw new ArgumentNullException(nameof(environmentName));
        _values = values ?? EmptyValues;
    }

    /// <inheritdoc />
    public string EnvironmentName { get; }

    /// <inheritdoc />
    public string? Value(string name) =>
        _values.TryGetValue(name, out var value) ? value : null;

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
    /// </remarks>
    public static IModuleEnvironment None { get; } = new ModuleEnvironment("");

    private sealed class ProcessModuleEnvironment : IModuleEnvironment {
        public string EnvironmentName =>
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ??
            Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ??
            "Production";

        public string? Value(string name) => Environment.GetEnvironmentVariable(name);
    }
}
