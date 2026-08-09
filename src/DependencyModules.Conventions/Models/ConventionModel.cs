using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;

namespace DependencyModules.Conventions.Models;

/// <summary>
/// Identifies a service type across open and closed constructions.
/// </summary>
/// <remarks>
/// Matching cannot compare <see cref="ITypeDefinition"/> directly when the convention names an open
/// generic. <c>typeof(IHandler&lt;,&gt;)</c> resolves to a definition whose type arguments are the
/// declaring type's parameters, and a candidate implements <c>IHandler&lt;CreateOrder, OrderId&gt;</c>
/// — structurally different, same service. A key built from namespace, name and arity is equal for
/// both, and is a string, so it costs nothing to keep in an incremental model.
/// </remarks>
public static class ConventionTypeKey {

    public static string For(ITypeDefinition type) {
        var arity = type.TypeArguments?.Count ?? 0;
        var name = string.IsNullOrEmpty(type.Namespace) ? type.Name : type.Namespace + "." + type.Name;

        return arity == 0 ? name : name + "`" + arity;
    }
}

/// <summary>
/// What a convention registers each match as.
/// </summary>
public enum ConventionRegisterAs {
    /// <summary>
    /// As the service type the convention matched through. The default.
    /// </summary>
    Interfaces,

    /// <summary>
    /// As the match's own concrete type.
    /// </summary>
    Self,

    /// <summary>
    /// As the concrete type and every interface it implements, sharing one instance — the contract
    /// <c>[CrossWireService]</c> already provides, reached through
    /// <see cref="ServiceRegistrationModel.CrossWire"/>.
    /// </summary>
    SelfAndInterfaces,

    /// <summary>
    /// As one service type named by <c>As&lt;T&gt;()</c>, whatever the match matched through.
    /// </summary>
    Explicit,

    /// <summary>
    /// As the interface named after the type: <c>Foo</c> as <c>IFoo</c>.
    /// </summary>
    MatchingInterface,
}

/// <summary>
/// One <c>WithName</c> or <c>WithoutName</c> filter.
/// </summary>
/// <param name="Pattern">
/// The glob as written. Kept as a string rather than a compiled <c>Regex</c>, which is not equatable
/// and would break the incremental cache; the matcher compiles one per convention, which is still
/// once per pattern rather than once per candidate.
/// </param>
/// <param name="Exclude">True for the <c>Without</c> form.</param>
public record NameFilterModel(string Pattern, bool Exclude) {

    /// <summary>
    /// True when the pattern is matched against the full name rather than the bare type name.
    /// </summary>
    public bool IsQualified => Pattern.IndexOf('.') >= 0;
}

/// <summary>
/// One namespace filter on a convention.
/// </summary>
/// <param name="Namespace">The namespace as written, or the namespace of the marker type.</param>
/// <param name="Exact">
/// True to match only this namespace. False also matches the namespaces beneath it, which is what
/// <c>InNamespaceOf</c> means and what people expect of a namespace filter.
/// </param>
/// <param name="Exclude">True for the <c>NotIn</c> forms.</param>
public record NamespaceFilterModel(string Namespace, bool Exact, bool Exclude) {

    /// <summary>
    /// Whether a type's namespace falls inside this filter, ignoring whether it includes or
    /// excludes.
    /// </summary>
    public bool Covers(string? candidateNamespace) {
        var value = candidateNamespace ?? "";

        if (Exact) {
            return string.Equals(value, Namespace, StringComparison.Ordinal);
        }

        // A prefix match has to stop at a namespace separator, or "MyApp.Order" would swallow
        // "MyApp.Ordering".
        return value.Equals(Namespace, StringComparison.Ordinal) ||
               (value.Length > Namespace.Length &&
                value[Namespace.Length] == '.' &&
                value.StartsWith(Namespace, StringComparison.Ordinal));
    }
}

/// <summary>
/// One <c>WithAttribute</c> or <c>WithoutAttribute</c> filter.
/// </summary>
/// <param name="TypeKey">
/// The attribute type's namespace, name and arity, as <see cref="ConventionTypeKey"/> renders it.
/// Compared against the same rendering of what the candidate carries, so both sides are resolved
/// and a namespace-qualified usage matches a plain one.
/// </param>
/// <param name="Exclude">True for the <c>Without</c> form.</param>
public record AttributeFilterModel(string TypeKey, bool Exclude);

/// <summary>
/// One <c>RegisterAll</c> declaration read out of a module's <c>Conventions</c> method.
/// </summary>
/// <param name="ServiceType">
/// The service type as written, or null for <c>RegisterAll()</c> — a convention selected by filters
/// rather than by assignability, which registers its matches as themselves.
/// </param>
/// <param name="DefinitionKey">
/// Namespace, name and arity, for matching. See <see cref="ConventionTypeKey"/>. Null with no
/// service type.
/// </param>
/// <param name="IsOpenGeneric">
/// True for <c>RegisterAll(typeof(IHandler&lt;,&gt;))</c>. An open convention matches any closing
/// and registers each candidate against the construction it actually implements; a closed one has
/// to match that exact construction, or <c>RegisterAll&lt;IHandler&lt;A, B&gt;&gt;()</c> would pick
/// up every other closing too.
/// </param>
/// <param name="Lifestyle">
/// Null when no <c>AsSingleton</c>/<c>AsScoped</c>/<c>AsTransient</c> was called. Deliberately not
/// defaulted — a lifetime nobody wrote down is the most expensive thing to get wrong, so this is
/// reported as DM0009 rather than guessed.
/// </param>
/// <param name="IncludeBaseClasses">Set by <c>IncludeBaseClasses()</c>.</param>
/// <param name="Location">Where the chain was written, for diagnostics.</param>
/// <param name="RegisterAs">Set by <c>AsSelf()</c> or <c>AsSelfWithInterfaces()</c>.</param>
/// <param name="NamespaceFilters">
/// Inclusions combine with <b>or</b>; exclusions are applied afterwards and any one of them removes
/// a match. Null when the convention did not filter by namespace.
/// </param>
/// <param name="RegistrationType">Set by <c>Using(...)</c>; null means <c>Add</c>.</param>
/// <param name="Key">Set by <c>WithKey(...)</c>, kept as it was written.</param>
/// <param name="KeyNamespaces">Namespaces the key expression needs, for an enum member.</param>
/// <param name="AttributeFilters">
/// Set by <c>WithAttribute</c> and <c>WithoutAttribute</c>. All of them have to hold.
/// </param>
public record ConventionModel(
    ITypeDefinition? ServiceType,
    string? DefinitionKey,
    bool IsOpenGeneric,
    ServiceLifestyle? Lifestyle,
    bool IncludeBaseClasses,
    LocationModel Location,
    ConventionRegisterAs RegisterAs = ConventionRegisterAs.Interfaces,
    IReadOnlyList<NamespaceFilterModel>? NamespaceFilters = null,
    RegistrationType? RegistrationType = null,
    object? Key = null,
    IReadOnlyList<string>? KeyNamespaces = null,
    IReadOnlyList<AttributeFilterModel>? AttributeFilters = null,
    IReadOnlyList<NameFilterModel>? NameFilters = null,
    ITypeDefinition? ExplicitServiceType = null) {

    /// <summary>
    /// Whether the attributes a candidate carries pass the filters.
    /// </summary>
    /// <remarks>
    /// Requirements combine with <b>and</b>: a type has to carry every attribute asked for and none
    /// of the excluded ones. Alternatives would need an or, which no Scrutor overload offers either.
    /// </remarks>
    public bool AttributesMatch(IReadOnlyList<string>? candidateAttributes) {
        if (AttributeFilters == null) {
            return true;
        }

        foreach (var filter in AttributeFilters) {
            var carried = candidateAttributes != null && candidateAttributes.Contains(filter.TypeKey);

            if (carried == filter.Exclude) {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The name to use in a diagnostic, whether or not a service type was named.
    /// </summary>
    public string DisplayName => ServiceType?.Name ?? "RegisterAll()";

    /// <summary>
    /// Whether a candidate's namespace passes the filters.
    /// </summary>
    public bool NamespaceMatches(string? candidateNamespace) {
        if (NamespaceFilters == null) {
            return true;
        }

        var included = false;
        var anyInclusion = false;

        foreach (var filter in NamespaceFilters) {
            if (filter.Exclude) {
                if (filter.Covers(candidateNamespace)) {
                    return false;
                }

                continue;
            }

            anyInclusion = true;
            included |= filter.Covers(candidateNamespace);
        }

        return !anyInclusion || included;
    }

    // Structural equality over the filter list; a positional record would compare it by reference
    // and never hit the incremental cache. See ModelEquality.
    public virtual bool Equals(ConventionModel? other) =>
        other is not null &&
        Equals(ServiceType, other.ServiceType) &&
        DefinitionKey == other.DefinitionKey &&
        IsOpenGeneric == other.IsOpenGeneric &&
        Lifestyle == other.Lifestyle &&
        IncludeBaseClasses == other.IncludeBaseClasses &&
        Location == other.Location &&
        RegisterAs == other.RegisterAs &&
        RegistrationType == other.RegistrationType &&
        Equals(Key, other.Key) &&
        ModelEquality.ListEquals(NamespaceFilters, other.NamespaceFilters) &&
        ModelEquality.ListEquals(KeyNamespaces, other.KeyNamespaces) &&
        ModelEquality.ListEquals(AttributeFilters, other.AttributeFilters) &&
        ModelEquality.ListEquals(NameFilters, other.NameFilters) &&
        Equals(ExplicitServiceType, other.ExplicitServiceType);

    public override int GetHashCode() {
        unchecked {
            var hash = ServiceType?.GetHashCode() ?? 0;
            hash = hash * 31 + (DefinitionKey?.GetHashCode() ?? 0);
            hash = hash * 31 + IsOpenGeneric.GetHashCode();
            hash = hash * 31 + Lifestyle.GetHashCode();
            hash = hash * 31 + IncludeBaseClasses.GetHashCode();
            hash = hash * 31 + (int)RegisterAs;
            hash = hash * 31 + (RegistrationType?.GetHashCode() ?? 0);
            hash = hash * 31 + (Key?.GetHashCode() ?? 0);
            hash = hash * 31 + ModelEquality.ListHashCode(NamespaceFilters);
            hash = hash * 31 + ModelEquality.ListHashCode(KeyNamespaces);
            hash = hash * 31 + ModelEquality.ListHashCode(AttributeFilters);
            hash = hash * 31 + ModelEquality.ListHashCode(NameFilters);
            hash = hash * 31 + (ExplicitServiceType?.GetHashCode() ?? 0);
            return hash;
        }
    }
}

/// <summary>
/// A statement in a <c>Conventions</c> body the generator could not read.
/// </summary>
/// <remarks>
/// Kept in the model rather than reported from the transform, because a transform runs inside the
/// incremental pipeline where reporting is not available. The text and the reason ride along so the
/// diagnostic can both quote what it refused and say why.
/// </remarks>
public record UnreadableStatementModel(string Text, string Reason, LocationModel Location);

/// <summary>
/// A module that implements <c>IConventionModule</c>, and everything read out of it.
/// </summary>
public record ConventionModuleModel(
    ITypeDefinition ModuleType,
    IReadOnlyList<ConventionModel> Conventions,
    IReadOnlyList<UnreadableStatementModel> Unreadable) {

    /// <summary>
    /// The sentinel for a declaration this generator does not own, matching how every other model
    /// in this codebase signals "nothing to do".
    /// </summary>
    public static readonly ConventionModuleModel Ignore = new(
        TypeDefinition.Get("", "Ignore"),
        Array.Empty<ConventionModel>(),
        Array.Empty<UnreadableStatementModel>());

    public bool IsIgnored => ReferenceEquals(this, Ignore) || ModuleType.Equals(Ignore.ModuleType);

    // Structural equality over the lists; a positional record would compare them by reference and
    // never hit the incremental cache. See ModelEquality.
    public virtual bool Equals(ConventionModuleModel? other) =>
        other is not null &&
        ModuleType.Equals(other.ModuleType) &&
        ModelEquality.ListEquals(Conventions, other.Conventions) &&
        ModelEquality.ListEquals(Unreadable, other.Unreadable);

    public override int GetHashCode() {
        unchecked {
            var hash = ModuleType.GetHashCode();
            hash = hash * 31 + ModelEquality.ListHashCode(Conventions);
            hash = hash * 31 + ModelEquality.ListHashCode(Unreadable);
            return hash;
        }
    }
}
