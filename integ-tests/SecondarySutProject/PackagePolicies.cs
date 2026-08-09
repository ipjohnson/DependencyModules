namespace SecondarySutProject;

// Types with no DependencyModules attributes, standing in for a third-party package: something a
// consuming project can scan with InAssemblyOf<T> because it cannot add a module to it.
//
// This project does not reference the conventions analyzer, so nothing here registers itself.

/// <summary>A policy contract a consumer might scan for.</summary>
public interface IPackagePolicy {
    /// <summary>Identifies the policy in assertions.</summary>
    string Name { get; }
}

/// <summary>A public policy, visible across the assembly boundary.</summary>
public class FirstPackagePolicy : IPackagePolicy {
    /// <inheritdoc />
    public string Name => "first";
}

/// <summary>A second public policy, so the scan has more than one match.</summary>
public class SecondPackagePolicy : IPackagePolicy {
    /// <inheritdoc />
    public string Name => "second";
}

// Internal, so it is invisible across the boundary — the difference between scanning metadata and
// scanning the compilation being built.
internal class HiddenPackagePolicy : IPackagePolicy {
    public string Name => "hidden";
}
