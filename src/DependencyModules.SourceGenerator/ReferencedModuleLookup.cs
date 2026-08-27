using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace DependencyModules.SourceGenerator;

/// <summary>
/// Finds the module a name refers to when the module was not declared in this compilation.
/// </summary>
/// <remarks>
/// <para>
/// A module generates an attribute implementing <c>IDependencyModuleProvider</c>, in the module's
/// own namespace. That interface is what makes a referenced module attribute recognisable: nothing
/// else implements it, so a type carrying it is a module's attribute and its containing namespace
/// is the module's namespace.
/// </para>
/// <para>
/// Two paths, deliberately unequal in cost. If the developer wrote the <c>using</c>, the attribute
/// resolves and the namespace is one of the file's imports — so trying each import by name is a
/// handful of dictionary lookups, and that is the case that happens on every successful build. If
/// it does not resolve, the build is already failing with <c>CS0246</c>, and only then is it worth
/// walking the referenced assemblies to find where the type actually lives, which is the whole
/// point of DM0016.
/// </para>
/// </remarks>
internal sealed class ReferencedModuleLookup {
    private const string ProviderInterface = "DependencyModules.Runtime.Interfaces.IDependencyModuleProvider";

    private readonly Compilation? _compilation;
    private readonly INamedTypeSymbol? _providerInterface;
    private Dictionary<string, string>? _byName;

    public ReferencedModuleLookup(Compilation? compilation) {
        _compilation = compilation;
        _providerInterface = compilation?.GetTypeByMetadataName(ProviderInterface);
    }

    /// <summary>
    /// The namespace of a module whose attribute is <paramref name="name"/> and which one of
    /// <paramref name="candidateNamespaces"/> brings into scope, or null.
    /// </summary>
    public string? FindImported(string name, IEnumerable<string> candidateNamespaces) {
        if (_compilation == null || _providerInterface == null) {
            return null;
        }

        foreach (var candidate in candidateNamespaces) {
            if (string.IsNullOrEmpty(candidate)) {
                continue;
            }

            foreach (var typeName in AttributeNames(name)) {
                var symbol = _compilation.GetTypeByMetadataName($"{candidate}.{typeName}");

                if (symbol != null && IsModuleAttribute(symbol)) {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The namespace of a module whose attribute is <paramref name="name"/>, wherever it lives.
    /// </summary>
    /// <remarks>
    /// Walks the referenced assemblies. Reached only for a usage that resolved to nothing, so the
    /// compilation it walks is one that is already failing to build.
    /// </remarks>
    public string? FindAnywhere(string name) {
        if (_compilation == null || _providerInterface == null) {
            return null;
        }

        _byName ??= BuildIndex();

        foreach (var typeName in AttributeNames(name)) {
            if (_byName.TryGetValue(typeName, out var moduleNamespace)) {
                return moduleNamespace;
            }
        }

        return null;
    }

    /// <summary>Written as <c>[assembly: Foo]</c> or <c>[assembly: FooAttribute]</c>.</summary>
    private static IEnumerable<string> AttributeNames(string name) {
        yield return name;

        if (!name.EndsWith("Attribute", System.StringComparison.Ordinal)) {
            yield return name + "Attribute";
        }
    }

    private bool IsModuleAttribute(INamedTypeSymbol symbol) =>
        symbol.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, _providerInterface));

    private Dictionary<string, string> BuildIndex() {
        var index = new Dictionary<string, string>(System.StringComparer.Ordinal);

        foreach (var reference in _compilation!.References) {
            if (_compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly) {
                continue;
            }

            Walk(assembly.GlobalNamespace, index);
        }

        return index;
    }

    private void Walk(INamespaceSymbol namespaceSymbol, Dictionary<string, string> index) {
        foreach (var type in namespaceSymbol.GetTypeMembers()) {
            if (type.DeclaredAccessibility == Accessibility.Public && IsModuleAttribute(type)) {
                // First wins. Two packages can ship a same-named module, and naming one of them is
                // more useful than naming neither.
                if (!index.ContainsKey(type.Name)) {
                    index.Add(type.Name, namespaceSymbol.ToDisplayString());
                }
            }
        }

        foreach (var nested in namespaceSymbol.GetNamespaceMembers()) {
            Walk(nested, index);
        }
    }
}
