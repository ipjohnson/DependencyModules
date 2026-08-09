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
/// One <c>RegisterAll</c> declaration read out of a module's <c>Conventions</c> method.
/// </summary>
/// <param name="ServiceType">The service type as written.</param>
/// <param name="DefinitionKey">Namespace, name and arity, for matching. See <see cref="ConventionTypeKey"/>.</param>
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
public record ConventionModel(
    ITypeDefinition ServiceType,
    string DefinitionKey,
    bool IsOpenGeneric,
    ServiceLifestyle? Lifestyle,
    bool IncludeBaseClasses,
    LocationModel Location);

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
