namespace DependencyModules.xUnit.Impl;

/// <summary>
/// Conversions between the two shapes xUnit uses for a trait dictionary.
/// </summary>
/// <remarks>
/// These replace <c>ToReadOnly</c> and <c>ToReadWrite</c> from <c>Xunit.Internal</c>. That
/// namespace is xUnit's own internal surface, not part of the extensibility contract, and carries
/// no compatibility guarantee across versions. Unlike the signature drift in the extensibility
/// interfaces — which the compiler catches — a behavioural change to an internal helper would
/// arrive as a wrong trait dictionary at run time. A dozen lines of dictionary copying is a
/// cheaper thing to own outright than that risk.
///
/// Deliberately not named <c>ToReadOnly</c>/<c>ToReadWrite</c>: if <c>Xunit.Internal</c> is ever
/// pulled back into scope, matching names would produce ambiguity rather than a clean choice.
/// <c>ToReadOnlyTraits</c> also avoids colliding with the <c>AsReadOnly</c> that
/// <see cref="System.Collections.Generic.CollectionExtensions"/> supplies for dictionaries.
/// </remarks>
internal static class TraitDictionaryExtensions {

    /// <summary>
    /// Widens the mutable form xUnit stores traits in to the read-only form its constructors take.
    /// </summary>
    /// <remarks>
    /// The key comparer is carried over, which is what the <c>Xunit.Internal</c> helper this
    /// replaces did — verified against 1.0.0 by running both over the same input and comparing.
    /// It is not observable further downstream, because <see cref="Xunit.v3.XunitTest"/> copies
    /// traits into a fresh dictionary under the default comparer whatever it is handed. Carried
    /// over regardless, so this is a faithful swap rather than one that merely happens to look
    /// equivalent under today's xUnit.
    /// </remarks>
    public static IReadOnlyDictionary<string, IReadOnlyCollection<string>> ToReadOnlyTraits(
        this Dictionary<string, HashSet<string>> traits) =>
        traits.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyCollection<string>)pair.Value,
            traits.Comparer);

    /// <summary>
    /// Copies the read-only form into the mutable one, under the supplied key comparer.
    /// </summary>
    /// <remarks>
    /// A copy rather than a view: the result is handed to a test case that owns its own traits and
    /// may add to them, and writing through to the method's metadata would leak across test cases.
    ///
    /// The comparer governs this intermediate dictionary only — <see cref="Xunit.v3.XunitTestCase"/>
    /// rebuilds what it is given under its own ordinal-ignore-case comparer, so passing a different
    /// one here changes nothing observable. It is supplied to keep the intermediate consistent with
    /// where the traits are headed, and to match the call this replaced.
    /// </remarks>
    public static Dictionary<string, HashSet<string>> ToWritableTraits(
        this IReadOnlyDictionary<string, IReadOnlyCollection<string>> traits,
        IEqualityComparer<string> comparer) {

        var result = new Dictionary<string, HashSet<string>>(comparer);

        foreach (var pair in traits) {
            result[pair.Key] = new HashSet<string>(pair.Value);
        }

        return result;
    }
}
