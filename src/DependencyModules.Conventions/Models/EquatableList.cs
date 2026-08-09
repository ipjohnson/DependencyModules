using DependencyModules.SourceGenerator.Impl.Models;

namespace DependencyModules.Conventions.Models;

/// <summary>
/// A list that compares by its contents, for use as an incremental provider's output.
/// </summary>
/// <remarks>
/// A plain list — and <c>ImmutableArray</c> — compares by reference of the underlying array, so an
/// unchanged walk still looks different on every run and everything downstream is recomputed. That
/// is measurable rather than theoretical: the metadata scan re-runs on every keystroke by
/// construction, so without this the emission would too.
/// </remarks>
public sealed class EquatableList<T> : IReadOnlyList<T> {
    private readonly IReadOnlyList<T> _items;

    public EquatableList(IReadOnlyList<T> items) {
        _items = items;
    }

    public int Count => _items.Count;

    public T this[int index] => _items[index];

    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        ((System.Collections.IEnumerable)_items).GetEnumerator();

    public override bool Equals(object? obj) =>
        obj is EquatableList<T> other && ModelEquality.ListEquals(_items, other._items);

    public override int GetHashCode() => ModelEquality.ListHashCode(_items);
}
