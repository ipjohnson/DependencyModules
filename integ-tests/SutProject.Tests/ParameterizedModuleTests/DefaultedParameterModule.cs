using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace SutProject.Tests.ParameterizedModuleTests;

public class DefaultedParameterValues(string label, int size) {
    public string Label { get; } = label;

    public int Size { get; } = size;
}

/// <summary>
/// A module whose parameters carry C# initialisers, one of each kind that matters.
/// </summary>
/// <remarks>
/// <c>Label</c> is a non-nullable reference type, which is the shape that used to lose its default:
/// the generated attribute assigned every property unconditionally unless it was declared nullable,
/// so composing this module without naming <c>Label</c> wrote null over <c>"default-label"</c> and
/// the failure surfaced as a NullReferenceException inside ConfigureServices.
///
/// <c>Size</c> is a value type and is included to pin the documented limit rather than a fix: an
/// attribute property of type <c>int</c> is 0 until assigned and 0 is a legitimate value, so null
/// cannot express "unset" for one. Its initialiser is not preserved, and that is the accepted
/// behaviour.
/// </remarks>
[DependencyModule]
public partial class DefaultedParameterModule : IServiceCollectionConfiguration {
    public string Label { get; set; } = "default-label";

    public int Size { get; set; } = 42;

    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton(new DefaultedParameterValues(Label, Size));
    }

    // Two composers below carry different values, so identity is the values rather than the type.
    public override bool Equals(object? obj) =>
        obj is DefaultedParameterModule other && other.Label == Label && other.Size == Size;

    public override int GetHashCode() => HashCode.Combine(Label, Size);
}

/// <summary>Composes the module above without naming either parameter.</summary>
[DependencyModule]
[DefaultedParameterModule]
public partial class DefaultedParameterComposer;

/// <summary>Composes it while naming both, which has always worked.</summary>
[DependencyModule]
[DefaultedParameterModule(Label = "named-label", Size = 7)]
public partial class NamedParameterComposer;
