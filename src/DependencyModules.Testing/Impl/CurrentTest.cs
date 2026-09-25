using System.Reflection;
using DependencyModules.Testing.Attributes.Interfaces;

namespace DependencyModules.Testing.Impl;

/// <summary>
/// The running test, read through the <see cref="ICurrentTestProvider"/> that the runner package
/// installed.
/// </summary>
/// <remarks>
/// <para>
/// DependencyModules.xUnit, DependencyModules.xUnit4 and DependencyModules.NUnit install their
/// provider before the first of their tests runs. Without a runner package <see cref="Provider"/> is
/// null, and every member answers as if no test were running.
/// </para>
/// <para>
/// Settable, so that a test of code which reads this can put a provider of its own in place. A runner
/// installs its provider only when none is set.
/// </para>
/// </remarks>
public static class CurrentTest
{
    /// <summary>
    /// The provider every member reads through, or null when no runner package has installed one.
    /// </summary>
    public static ICurrentTestProvider? Provider { get; set; }

    /// <inheritdoc cref="ICurrentTestProvider.Key"/>
    public static object? Key => Provider?.Key;

    /// <inheritdoc cref="ICurrentTestProvider.DisplayName"/>
    public static string? DisplayName => Provider?.DisplayName;

    /// <inheritdoc cref="ICurrentTestProvider.Assembly"/>
    public static Assembly? Assembly => Provider?.Assembly;

    /// <inheritdoc cref="ICurrentTestProvider.TryWriteLine"/>
    public static bool TryWriteLine(string message) => Provider?.TryWriteLine(message) ?? false;
}
