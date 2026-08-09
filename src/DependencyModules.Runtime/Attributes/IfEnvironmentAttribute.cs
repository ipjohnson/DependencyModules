namespace DependencyModules.Runtime.Attributes;

/// <summary>
/// Registers the service only when the environment name matches one of the given names.
/// </summary>
/// <remarks>
/// <para>
/// Read at compile time and turned into a run-time test around the registration, so the type is
/// still compiled and still referenced — a condition changes what is registered, not what ships.
/// Trimming a service out of a build is a compile-time decision and belongs to <c>#if</c>.
/// </para>
/// <para>
/// Names are compared case-insensitively, matching <c>IHostEnvironment.IsDevelopment()</c>.
/// Alternatives go in one attribute rather than several: conditions of different kinds combine with
/// <b>and</b>, so two of these could never both hold.
/// </para>
/// <example>
/// <code>
/// [SingletonService]
/// [IfEnvironment("Development", "Staging")]
/// public class FakeEmailSender : IEmailSender { }
/// </code>
/// </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class IfEnvironmentAttribute : Attribute {
    /// <summary>
    /// Registers the service only in the given environments.
    /// </summary>
    /// <param name="environmentNames">The environment names to register in.</param>
    public IfEnvironmentAttribute(params string[] environmentNames) {
        EnvironmentNames = environmentNames;
    }

    /// <summary>
    /// The environment names this registration is limited to.
    /// </summary>
    public string[] EnvironmentNames { get; }
}
