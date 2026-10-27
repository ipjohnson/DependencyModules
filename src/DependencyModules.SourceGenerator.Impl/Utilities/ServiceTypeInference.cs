using CSharpAuthor;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

/// <summary>
/// The service type a <c>[SingletonService]</c>, <c>[ScopedService]</c> or <c>[TransientService]</c>
/// registers when the attribute has no <c>As</c>.
/// </summary>
/// <remarks>
/// <para>
/// On a class or record, the inference reads the base list of the declaration that carries the
/// attribute, in written order. The first interface that is not a
/// <see cref="_capabilityInterfaces">capability</see> wins. When no written interface qualifies,
/// the interfaces each base class declares are tried, from the nearest base class outward. When
/// none qualifies, the implementation is registered as itself. Only the attributed declaration's
/// base list counts. An interface written on another part of a partial class is not seen.
/// </para>
/// <para>
/// On a static factory method, nothing is inferred. The service type is the method's declared
/// return type, as written. A method that returns <c>Thing</c> registers <c>Thing</c>, even when
/// <c>Thing</c> implements <c>IThing</c>. Pass an empty base list and the return type to
/// <see cref="GetServiceType"/> to get this result.
/// </para>
/// <para>
/// An open generic interface has its type parameters replaced by empty arguments, so it is
/// registered as <c>IGeneric&lt;&gt;</c>. The same replacement applies to an open generic type
/// named in <c>As</c>.
/// </para>
/// </remarks>
public static class ServiceTypeInference
{
    /// <summary>
    /// Interfaces that describe a capability rather than a role, keyed by namespace and name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Passed over when choosing a service type nobody named. Writing <c>: IDisposable</c> says the
    /// class cleans up after itself; it does not say <c>IDisposable</c> is what callers ask for.
    /// Without this, <c>class OrderedPool : IDisposable, IPool</c> registers as <c>IDisposable</c>,
    /// because the first interface in the declaration wins.
    /// </para>
    /// <para>
    /// A list rather than the namespace rule <c>AsSelfWithInterfaces</c> uses, because the two are
    /// not the same problem. That expansion is additive — excluding too much costs a bonus
    /// registration. This is an exclusive choice, so excluding too much means the interface the
    /// developer wanted is not registered at all. <c>System</c> holds plenty of interfaces that are
    /// genuinely service roles: <c>IEqualityComparer&lt;T&gt;</c>, <c>IJsonTypeInfoResolver</c>,
    /// <c>IHttpClientFactory</c>. Precision matters more here than a rule stated in one sentence.
    /// </para>
    /// <para>
    /// The list is short and stays short. These are the BCL's language and framework integration
    /// points, and the set has barely moved in twenty years — unlike the open-ended set of
    /// interfaces a type happens to reach, which is what makes the namespace rule right over there.
    /// </para>
    /// <para>
    /// <c>IEnumerable</c> earns its place twice over: registering a service as
    /// <c>IEnumerable&lt;T&gt;</c> collides with how the container represents "every registration of
    /// T".
    /// </para>
    /// <para>
    /// A service type the developer names is untouched — <c>[SingletonService(As =
    /// typeof(IDisposable))]</c> still registers <c>IDisposable</c>. This governs inference only.
    /// </para>
    /// </remarks>
    private static readonly HashSet<string> _capabilityInterfaces = new()
    {
        "System.IDisposable",
        "System.IAsyncDisposable",
        "System.ICloneable",
        "System.IComparable", // covers IComparable<T>, same name
        "System.IEquatable",
        "System.IConvertible",
        "System.IFormattable",
        "System.ISpanFormattable",
        "System.IParsable",
        "System.ISpanParsable",
        "System.Collections.IEnumerable",
        "System.Collections.Generic.IEnumerable",
        "System.Runtime.Serialization.ISerializable",
        "System.ComponentModel.INotifyPropertyChanged",
        "System.ComponentModel.INotifyPropertyChanging",
        "System.Collections.Specialized.INotifyCollectionChanged",
    };

    /// <summary>
    /// The service type to register when the attribute has no <c>As</c>.
    /// </summary>
    /// <param name="implementation">
    /// The attributed class, or the return type of the attributed factory method.
    /// </param>
    /// <param name="baseListTypes">
    /// The types in the attributed declaration's base list, in written order, as
    /// <see cref="BaseListSymbols"/> resolves them. Empty for a factory method.
    /// </param>
    public static ITypeDefinition GetServiceType(
        ITypeDefinition implementation,
        IEnumerable<INamedTypeSymbol> baseListTypes
    )
    {
        return GetBaseTypeRegistration(baseListTypes) ?? implementation;
    }

    /// <summary>
    /// The types in a declaration's base list that resolve to a named type, in written order.
    /// </summary>
    /// <remarks>
    /// Resolved lazily, so <see cref="GetServiceType"/> stops binding at the first interface it
    /// takes.
    /// </remarks>
    public static IEnumerable<INamedTypeSymbol> BaseListSymbols(
        TypeDeclarationSyntax typeDeclarationSyntax,
        SemanticModel semanticModel
    )
    {
        if (typeDeclarationSyntax.BaseList == null)
        {
            yield break;
        }

        foreach (var baseTypeSyntax in typeDeclarationSyntax.BaseList.Types)
        {
            var symbolInfo = ModelExtensions.GetSymbolInfo(semanticModel, baseTypeSyntax.Type);

            if (symbolInfo.Symbol is INamedTypeSymbol namedTypeSymbol)
            {
                yield return namedTypeSymbol;
            }
        }
    }

    /// <summary>
    /// The first declared interface that is not a <see cref="_capabilityInterfaces">capability</see>,
    /// else the first one a base class provides.
    /// </summary>
    private static ITypeDefinition? GetBaseTypeRegistration(
        IEnumerable<INamedTypeSymbol> baseListTypes
    )
    {
        INamedTypeSymbol? baseClassSymbol = null;

        foreach (var namedTypeSymbol in baseListTypes)
        {
            var baseTypeDefinition = namedTypeSymbol.GetTypeDefinitionFromNamedSymbol();

            // only auto register interfaces
            if (
                baseTypeDefinition is { TypeDefinitionEnum: TypeDefinitionEnum.InterfaceDefinition }
            )
            {
                // Passed over rather than remembered: a skipped interface must not become the
                // symbol walked below, or IEnumerable<int> would hand back IEnumerable.
                if (SkipInterface(baseTypeDefinition))
                {
                    continue;
                }

                if (baseTypeDefinition is GenericTypeDefinition)
                {
                    baseTypeDefinition = ReplaceGenericParametersForRegistration(
                        baseTypeDefinition
                    );
                }

                return baseTypeDefinition;
            }

            baseClassSymbol = namedTypeSymbol;
        }

        if (baseClassSymbol != null)
        {
            return GetBaseInterface(baseClassSymbol);
        }

        return null;
    }

    private static ITypeDefinition? GetBaseInterface(INamedTypeSymbol baseTypeSymbol)
    {
        foreach (var interfaceSymbol in baseTypeSymbol.Interfaces)
        {
            var interfaceType = interfaceSymbol.GetTypeDefinitionFromNamedSymbol();

            // only auto register interfaces
            if (interfaceType == null || SkipInterface(interfaceType))
            {
                continue;
            }

            if (interfaceType is GenericTypeDefinition)
            {
                interfaceType = ReplaceGenericParametersForRegistration(interfaceType);
            }

            return interfaceType;
        }

        if (baseTypeSymbol.BaseType == null)
        {
            return null;
        }

        return GetBaseInterface(baseTypeSymbol.BaseType);
    }

    /// <summary>
    /// Whether an interface is passed over when choosing a service type nobody named.
    /// </summary>
    /// <remarks>
    /// Matched on namespace and name so one entry covers a generic and its closings —
    /// <c>IEquatable&lt;Money&gt;</c> and <c>IEquatable&lt;T&gt;</c> both render as
    /// <c>System.IEquatable</c>.
    /// </remarks>
    private static bool SkipInterface(ITypeDefinition interfaceType) =>
        _capabilityInterfaces.Contains($"{interfaceType.Namespace}.{interfaceType.Name}");

    /// <summary>
    /// The generic type with each type parameter replaced by an empty argument, so it renders as
    /// an open generic such as <c>IGeneric&lt;&gt;</c>.
    /// </summary>
    public static ITypeDefinition ReplaceGenericParametersForRegistration(
        ITypeDefinition registration
    )
    {
        var argumentTypes = registration
            .TypeArguments.Select(_ =>
                _ is TypeParameterDefinition ? TypeDefinition.Get("", "") : _
            )
            .ToArray();

        registration = new GenericTypeDefinition(
            registration.TypeDefinitionEnum,
            registration.Namespace,
            registration.Name,
            argumentTypes
        );

        return registration;
    }
}
