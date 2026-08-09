using CSharpAuthor;
using DependencyModules.Conventions.Models;
using DependencyModules.SourceGenerator.Impl.Models;
using DependencyModules.SourceGenerator.Impl.Utilities;
using Microsoft.CodeAnalysis;

namespace DependencyModules.Conventions.Utilities;

/// <summary>
/// Reads convention candidates out of a referenced assembly's metadata.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart to <see cref="ConventionCandidateUtility"/>, which reads the compilation being
/// built. Everything here comes from symbols rather than syntax, because a referenced assembly has
/// no syntax to read — that is the whole difference, and it is why the constructor path is separate
/// rather than shared.
/// </para>
/// <para>
/// <b>What bounds the cost is the convention naming the assembly.</b> Walking every reference visits
/// thousands of types on every keystroke; walking one named assembly visits its own. A reference is
/// therefore rejected on <see cref="IAssemblySymbol.Name"/> before any type is touched. There is no
/// way to ask for all of them, and there should not be one.
/// </para>
/// <para>
/// Only <c>public</c> types are visible here. A scan of the compilation being built also sees
/// <c>internal</c> ones, and nothing can report the type it cannot see, so that difference is
/// documented rather than diagnosed.
/// </para>
/// </remarks>
public static class MetadataCandidateUtility {

    /// <summary>
    /// Candidates from every assembly the given conventions name.
    /// </summary>
    /// <remarks>
    /// Returns an empty list when nothing names an assembly, which is the common case and costs one
    /// pass over the convention list.
    /// </remarks>
    public static IReadOnlyList<ConventionCandidateModel> Collect(
        IReadOnlyList<ConventionModuleModel> conventionModules,
        Compilation compilation,
        CancellationToken cancellationToken) {

        var wanted = new HashSet<string>(StringComparer.Ordinal);

        foreach (var module in conventionModules) {
            foreach (var convention in module.Conventions) {
                if (convention.AssemblyName != null) {
                    wanted.Add(convention.AssemblyName);
                }
            }
        }

        if (wanted.Count == 0) {
            return Array.Empty<ConventionCandidateModel>();
        }

        var candidates = new List<ConventionCandidateModel>();

        foreach (var reference in compilation.References) {
            cancellationToken.ThrowIfCancellationRequested();

            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly ||
                !wanted.Contains(assembly.Name)) {
                continue;
            }

            CollectFromNamespace(assembly.GlobalNamespace, assembly.Name, candidates, cancellationToken);
        }

        return candidates;
    }

    private static void CollectFromNamespace(
        INamespaceSymbol namespaceSymbol,
        string assemblyName,
        List<ConventionCandidateModel> candidates,
        CancellationToken cancellationToken) {

        foreach (var member in namespaceSymbol.GetMembers()) {
            cancellationToken.ThrowIfCancellationRequested();

            switch (member) {
                case INamespaceSymbol nested:
                    CollectFromNamespace(nested, assemblyName, candidates, cancellationToken);
                    break;

                case INamedTypeSymbol type when IsCandidate(type):
                    candidates.Add(BuildCandidate(type, assemblyName));
                    break;
            }
        }
    }

    /// <summary>
    /// The metadata counterpart of the syntactic predicate: a constructible public class that has
    /// not already declared how it is registered.
    /// </summary>
    /// <remarks>
    /// The attribute exclusion matters more here than it looks. An assembly whose types carry these
    /// attributes has its own module and registers them itself, so scanning it as well would
    /// register everything twice, possibly under a different lifetime — and composing that module is
    /// what the developer should be doing instead. A decorator is excluded for the same reason it is
    /// in the compilation being built: it implements the interface it decorates, so a convention
    /// scanning that interface would match the decorator and register it as a service.
    /// </remarks>
    private static bool IsCandidate(INamedTypeSymbol type) =>
        type.TypeKind == TypeKind.Class &&
        !type.IsAbstract &&
        !type.IsStatic &&
        type.DeclaredAccessibility == Accessibility.Public &&
        !DeclaresRegistration(type);

    private static bool DeclaresRegistration(INamedTypeSymbol type) {
        foreach (var attribute in type.GetAttributes()) {
            var name = attribute.AttributeClass?.Name;

            if (name != null && Array.IndexOf(ExcludedAttributeNames, name) >= 0) {
                return true;
            }
        }

        return false;
    }

    private static readonly string[] ExcludedAttributeNames = {
        "SingletonServiceAttribute",
        "ScopedServiceAttribute",
        "TransientServiceAttribute",
        "CrossWireServiceAttribute",
        "DecoratorAttribute",
    };

    private static ConventionCandidateModel BuildCandidate(INamedTypeSymbol type, string assemblyName) {
        var declared = new List<ImplementedInterfaceModel>();
        var viaBaseClass = new List<ImplementedInterfaceModel>();
        var seen = new HashSet<ITypeDefinition>();

        // Directly implemented interfaces, and what those extend, are the metadata equivalent of
        // "written on the declaration". Anything else in AllInterfaces arrived through a base class,
        // which is the distinction IncludeBaseClasses turns on.
        foreach (var interfaceSymbol in type.Interfaces) {
            Add(declared, seen, interfaceSymbol, null);

            foreach (var inherited in interfaceSymbol.AllInterfaces) {
                Add(declared, seen, inherited, interfaceSymbol.Name);
            }
        }

        foreach (var interfaceSymbol in type.AllInterfaces) {
            Add(viaBaseClass, seen, interfaceSymbol, type.BaseType?.Name);
        }

        return new ConventionCandidateModel(
            type.GetTypeDefinition(),
            declared,
            viaBaseClass,
            GreediestConstructor(type),
            type.InstanceConstructors.Any(c => c.DeclaredAccessibility == Accessibility.Public),
            LocationModel.None,
            null,
            AttributeKeysOf(type),
            assemblyName);
    }

    private static void Add(
        List<ImplementedInterfaceModel> target,
        HashSet<ITypeDefinition> seen,
        INamedTypeSymbol interfaceSymbol,
        string? viaTypeName) {

        var definition = interfaceSymbol.GetTypeDefinition();

        if (!seen.Add(definition)) {
            return;
        }

        target.Add(new ImplementedInterfaceModel(
            definition, ConventionTypeKey.For(definition), viaTypeName));
    }

    /// <summary>
    /// The constructor the container would pick, read from symbols.
    /// </summary>
    /// <remarks>
    /// The greediest public one, matching what the syntax path does within a declaration.
    /// Parameter attributes are not carried across: they exist to drive <c>[FromKeyedServices]</c>
    /// on code being generated alongside, and a package's own constructor parameters are not that.
    /// </remarks>
    private static ConstructorInfoModel? GreediestConstructor(INamedTypeSymbol type) {
        IMethodSymbol? greediest = null;

        foreach (var constructor in type.InstanceConstructors) {
            if (constructor.DeclaredAccessibility != Accessibility.Public) {
                continue;
            }

            if (greediest == null || constructor.Parameters.Length > greediest.Parameters.Length) {
                greediest = constructor;
            }
        }

        if (greediest == null) {
            return null;
        }

        var parameters = new List<ParameterInfoModel>(greediest.Parameters.Length);

        foreach (var parameter in greediest.Parameters) {
            parameters.Add(new ParameterInfoModel(
                parameter.Name,
                parameter.Type.GetTypeDefinition(),
                parameter.HasExplicitDefaultValue ? parameter.ExplicitDefaultValue : null,
                Array.Empty<AttributeModel>()));
        }

        return new ConstructorInfoModel(parameters);
    }

    private static IReadOnlyList<string>? AttributeKeysOf(INamedTypeSymbol type) {
        var attributes = type.GetAttributes();

        if (attributes.Length == 0) {
            return null;
        }

        var keys = new List<string>(attributes.Length);

        foreach (var attribute in attributes) {
            if (attribute.AttributeClass is { } attributeClass) {
                keys.Add(ConventionTypeKey.For(attributeClass.GetTypeDefinition()));
            }
        }

        return keys.Count > 0 ? keys : null;
    }
}
