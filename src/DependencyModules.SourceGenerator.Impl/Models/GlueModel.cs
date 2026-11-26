namespace DependencyModules.SourceGenerator.Impl.Models;

public enum GlueItemKind
{
    Map,
    Set,
    TypeList,
    ValueList,
}

/// <summary>
/// One <c>Into*</c> attribute on a class.
/// </summary>
/// <param name="ItemType">The item type of the collection, as it is written in generated code.</param>
/// <param name="KeyType">The key type of a map, or the order type of a list with order values.</param>
/// <param name="Value">The key of a map item, or the order value of a list item, as a C# expression.</param>
/// <param name="ValueDisplay">The same value as the developer wrote it, for messages.</param>
/// <param name="DependsOn">The <c>dependsOn</c> arguments, each as a C# expression.</param>
/// <param name="Realm">The full name of the module that the attribute names, or null.</param>
/// <param name="Problem">Why the class cannot be an item of this collection, or null.</param>
public record GlueItemModel(
    GlueItemKind Kind,
    string ItemType,
    string? KeyType,
    string? Value,
    string? ValueDisplay,
    IReadOnlyList<string> DependsOn,
    string? Realm,
    string? Problem
)
{
    public virtual bool Equals(GlueItemModel? other) =>
        other is not null
        && Kind == other.Kind
        && ItemType == other.ItemType
        && KeyType == other.KeyType
        && Value == other.Value
        && ValueDisplay == other.ValueDisplay
        && Realm == other.Realm
        && Problem == other.Problem
        && ModelEquality.ListEquals(DependsOn, other.DependsOn);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = (int)Kind;
            hash = hash * 31 + ItemType.GetHashCode();
            hash = hash * 31 + (Value?.GetHashCode() ?? 0);
            hash = hash * 31 + ModelEquality.ListHashCode(DependsOn);
            return hash;
        }
    }
}

/// <summary>
/// A <c>Func</c> registration.
/// </summary>
/// <param name="FuncType">The closed delegate type, as it is written in generated code.</param>
/// <param name="Factory">The lambda that makes the delegate from <c>provider</c>.</param>
public record GlueFuncModel(string FuncType, string Factory);

/// <summary>
/// A <c>Func</c> parameter that the generator cannot make a registration for.
/// </summary>
public record GlueFuncProblemModel(string FuncType, string Reason);

/// <summary>
/// What the glue reads from one class or factory method.
/// </summary>
/// <param name="Construction">
/// The expression that builds the class from <c>provider</c>. Null when the class has no
/// <c>Into*</c> attribute or cannot be built.
/// </param>
/// <param name="ConstructionProblem">Why the class cannot be built, or null.</param>
/// <param name="FuncRealms">
/// The realms of the attributes of the class. They select the modules that register
/// <paramref name="Funcs"/>. A null entry is an attribute with no realm.
/// </param>
public record GlueClassModel(
    string TypeName,
    string DisplayName,
    LocationModel Location,
    IReadOnlyList<GlueItemModel> Items,
    string? Construction,
    string? ConstructionProblem,
    IReadOnlyList<GlueFuncModel> Funcs,
    IReadOnlyList<GlueFuncProblemModel> FuncProblems,
    IReadOnlyList<string?> FuncRealms,
    IReadOnlyList<EnvironmentConditionModel>? Conditions
)
{
    public static readonly GlueClassModel Ignore = new(
        "",
        "",
        LocationModel.None,
        Array.Empty<GlueItemModel>(),
        null,
        null,
        Array.Empty<GlueFuncModel>(),
        Array.Empty<GlueFuncProblemModel>(),
        Array.Empty<string?>(),
        null
    );

    public bool IsIgnored => TypeName.Length == 0;
}

public class GlueClassModelComparer : IEqualityComparer<GlueClassModel>
{
    // Location is absent for the reason ModuleEntryPointModelComparer gives: it moves with each
    // edit above the class, and this comparer is the incremental cache key.
    public bool Equals(GlueClassModel? x, GlueClassModel? y)
    {
        if (x is null || y is null)
        {
            return x is null && y is null;
        }

        return x.TypeName == y.TypeName
            && x.DisplayName == y.DisplayName
            && x.Construction == y.Construction
            && x.ConstructionProblem == y.ConstructionProblem
            && ModelEquality.ListEquals(x.Items, y.Items)
            && ModelEquality.ListEquals(x.Funcs, y.Funcs)
            && ModelEquality.ListEquals(x.FuncProblems, y.FuncProblems)
            && ModelEquality.ListEquals(x.FuncRealms, y.FuncRealms)
            && ModelEquality.ListEquals(x.Conditions, y.Conditions);
    }

    public int GetHashCode(GlueClassModel obj)
    {
        unchecked
        {
            var hash = obj.TypeName.GetHashCode();
            hash = hash * 31 + (obj.Construction?.GetHashCode() ?? 0);
            hash = hash * 31 + ModelEquality.ListHashCode(obj.Items);
            hash = hash * 31 + ModelEquality.ListHashCode(obj.Funcs);
            return hash;
        }
    }
}
