using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyModules.Runtime.Helpers;

/// <summary>
/// The items of one collection instance. Builds an item when it is first read, keeps it, and
/// disposes the items that it built.
/// </summary>
internal sealed class GlueItems<T> : IDisposable, IAsyncDisposable
{
    private readonly Func<IServiceProvider, T>[] _factories;
    private readonly IServiceProvider _provider;
    private readonly object _lock = new();
    private T[]? _items;
    private bool[]? _built;

    public GlueItems(Func<IServiceProvider, T>[] factories, IServiceProvider provider)
    {
        _factories = factories;
        _provider = provider;
    }

    public int Count => _factories.Length;

    public T Get(int index)
    {
        lock (_lock)
        {
            _items ??= new T[_factories.Length];
            _built ??= new bool[_factories.Length];

            if (!_built[index])
            {
                _items[index] = _factories[index](_provider);
                _built[index] = true;
            }

            return _items[index];
        }
    }

    public IEnumerable<T> All()
    {
        for (var i = 0; i < _factories.Length; i++)
        {
            yield return Get(i);
        }
    }

    public void Dispose()
    {
        foreach (var item in TakeBuilt())
        {
            switch (item)
            {
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
                case IAsyncDisposable asyncDisposable:
                    asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    break;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var item in TakeBuilt())
        {
            switch (item)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }
    }

    /// <summary>
    /// The built items, last built position first. A second call gives none.
    /// </summary>
    private List<object> TakeBuilt()
    {
        var built = new List<object>();

        lock (_lock)
        {
            if (_items != null && _built != null)
            {
                for (var i = _items.Length - 1; i >= 0; i--)
                {
                    if (_built[i] && _items[i] is { } item)
                    {
                        built.Add(item);
                    }

                    _built[i] = false;
                    _items[i] = default!;
                }
            }
        }

        return built;
    }
}

internal sealed class GlueList<T> : IReadOnlyList<T>, IDisposable, IAsyncDisposable
{
    private readonly GlueItems<T> _items;

    public GlueList(Func<IServiceProvider, T>[] factories, IServiceProvider provider)
    {
        _items = new GlueItems<T>(factories, provider);
    }

    public int Count => _items.Count;

    public T this[int index]
    {
        get
        {
            if (index < 0 || index >= _items.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _items.Get(index);
        }
    }

    public IEnumerator<T> GetEnumerator() => _items.All().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Dispose() => _items.Dispose();

    public ValueTask DisposeAsync() => _items.DisposeAsync();
}

/// <remarks>
/// A set compares its items, so it builds all of them when it is first read.
/// </remarks>
internal sealed class GlueSet<T> : IReadOnlySet<T>, IDisposable, IAsyncDisposable
{
    private readonly GlueItems<T> _items;
    private readonly object _lock = new();
    private HashSet<T>? _set;

    public GlueSet(Func<IServiceProvider, T>[] factories, IServiceProvider provider)
    {
        _items = new GlueItems<T>(factories, provider);
    }

    private HashSet<T> Set
    {
        get
        {
            lock (_lock)
            {
                return _set ??= new HashSet<T>(_items.All());
            }
        }
    }

    public int Count => Set.Count;

    public bool Contains(T item) => Set.Contains(item);

    public bool IsProperSubsetOf(IEnumerable<T> other) => Set.IsProperSubsetOf(other);

    public bool IsProperSupersetOf(IEnumerable<T> other) => Set.IsProperSupersetOf(other);

    public bool IsSubsetOf(IEnumerable<T> other) => Set.IsSubsetOf(other);

    public bool IsSupersetOf(IEnumerable<T> other) => Set.IsSupersetOf(other);

    public bool Overlaps(IEnumerable<T> other) => Set.Overlaps(other);

    public bool SetEquals(IEnumerable<T> other) => Set.SetEquals(other);

    public IEnumerator<T> GetEnumerator() => Set.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Dispose() => _items.Dispose();

    public ValueTask DisposeAsync() => _items.DisposeAsync();
}

/// <remarks>
/// <c>Keys</c>, <c>Count</c> and <c>ContainsKey</c> build no item. A lookup builds one item.
/// <c>Values</c> and the enumerator build each item that they give.
/// </remarks>
internal sealed class GlueMap<TKey, TValue>
    : IReadOnlyDictionary<TKey, TValue>,
        IDisposable,
        IAsyncDisposable
    where TKey : notnull
{
    private readonly GlueMapSnapshot<TKey, TValue> _snapshot;
    private readonly GlueItems<TValue> _items;

    public GlueMap(GlueMapSnapshot<TKey, TValue> snapshot, IServiceProvider provider)
    {
        _snapshot = snapshot;
        _items = new GlueItems<TValue>(snapshot.Factories, provider);
    }

    public int Count => _snapshot.Keys.Count;

    public IEnumerable<TKey> Keys => _snapshot.Keys;

    public IEnumerable<TValue> Values => _items.All();

    public TValue this[TKey key] =>
        TryGetValue(key, out var value)
            ? value
            : throw new KeyNotFoundException($"The map has no item with the key '{key}'.");

    public bool ContainsKey(TKey key) => _snapshot.Index.ContainsKey(key);

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        if (_snapshot.Index.TryGetValue(key, out var index))
        {
            value = _items.Get(index);
            return true;
        }

        value = default;
        return false;
    }

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        for (var i = 0; i < _snapshot.Keys.Count; i++)
        {
            yield return new KeyValuePair<TKey, TValue>(_snapshot.Keys[i], _items.Get(i));
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Dispose() => _items.Dispose();

    public ValueTask DisposeAsync() => _items.DisposeAsync();
}

internal sealed record GlueMapSnapshot<TKey, TValue>(
    IReadOnlyList<TKey> Keys,
    Dictionary<TKey, int> Index,
    Func<IServiceProvider, TValue>[] Factories
)
    where TKey : notnull;

internal sealed class GlueMapBuilder<TKey, TValue> : IGlueCollection
    where TKey : notnull
{
    private readonly object _lock = new();
    private readonly Dictionary<
        TKey,
        (Type Type, Func<IServiceProvider, TValue> Factory)
    > _entries = new();
    private GlueMapSnapshot<TKey, TValue>? _snapshot;

    public string Scheme => "IntoMap";

    public ServiceDescriptor Descriptor =>
        new(
            typeof(IReadOnlyDictionary<TKey, TValue>),
            provider => new GlueMap<TKey, TValue>(Snapshot(), provider),
            ServiceLifetime.Transient
        );

    public void Add(TKey key, Type implementationType, Func<IServiceProvider, TValue> factory)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                // Each module of a project that is not realm-only adds the same item.
                if (existing.Type == implementationType)
                {
                    return;
                }

                throw new InvalidOperationException(
                    $"The map {GlueNames.Of(typeof(IReadOnlyDictionary<TKey, TValue>))} has two "
                        + $"items with the key '{key}': {GlueNames.Of(existing.Type)} and "
                        + $"{GlueNames.Of(implementationType)}."
                );
            }

            _entries[key] = (implementationType, factory);
            _snapshot = null;
        }
    }

    public void Validate() { }

    private GlueMapSnapshot<TKey, TValue> Snapshot()
    {
        lock (_lock)
        {
            if (_snapshot != null)
            {
                return _snapshot;
            }

            var entries = _entries.ToArray();
            var comparer = GlueOrder.Comparer<TKey>();

            Array.Sort(
                entries,
                (x, y) =>
                {
                    var byKey = comparer?.Compare(x.Key, y.Key) ?? 0;

                    return byKey != 0
                        ? byKey
                        : StringComparer.Ordinal.Compare(
                            GlueNames.Of(x.Value.Type),
                            GlueNames.Of(y.Value.Type)
                        );
                }
            );

            var keys = new TKey[entries.Length];
            var index = new Dictionary<TKey, int>(entries.Length);
            var factories = new Func<IServiceProvider, TValue>[entries.Length];

            for (var i = 0; i < entries.Length; i++)
            {
                keys[i] = entries[i].Key;
                index[entries[i].Key] = i;
                factories[i] = entries[i].Value.Factory;
            }

            return _snapshot = new GlueMapSnapshot<TKey, TValue>(keys, index, factories);
        }
    }
}

internal sealed class GlueSetBuilder<T> : IGlueCollection
{
    private readonly object _lock = new();
    private readonly List<(Type Type, Func<IServiceProvider, T> Factory)> _entries = new();
    private Func<IServiceProvider, T>[]? _factories;

    public string Scheme => "IntoSet";

    public ServiceDescriptor Descriptor =>
        new(
            typeof(IReadOnlySet<T>),
            provider => new GlueSet<T>(Factories(), provider),
            ServiceLifetime.Transient
        );

    public void Add(Type implementationType, Func<IServiceProvider, T> factory)
    {
        lock (_lock)
        {
            foreach (var entry in _entries)
            {
                if (entry.Type == implementationType)
                {
                    return;
                }
            }

            _entries.Add((implementationType, factory));
            _factories = null;
        }
    }

    public void Validate() { }

    private Func<IServiceProvider, T>[] Factories()
    {
        lock (_lock)
        {
            return _factories ??= _entries
                .OrderBy(entry => GlueNames.Of(entry.Type), StringComparer.Ordinal)
                .Select(entry => entry.Factory)
                .ToArray();
        }
    }
}

internal abstract class GlueListBuilder<T> : IGlueCollection
{
    protected readonly object Lock = new();
    private Func<IServiceProvider, T>[]? _factories;

    public abstract string Scheme { get; }

    public ServiceDescriptor Descriptor =>
        new(
            typeof(IReadOnlyList<T>),
            provider => new GlueList<T>(Factories(), provider),
            ServiceLifetime.Transient
        );

    public void Validate() => Factories();

    protected void Changed() => _factories = null;

    /// <summary>The factories of the items, in the order of the list.</summary>
    protected abstract Func<IServiceProvider, T>[] Sort();

    private Func<IServiceProvider, T>[] Factories()
    {
        lock (Lock)
        {
            return _factories ??= Sort();
        }
    }
}

internal sealed class GlueTypeListBuilder<T> : GlueListBuilder<T>
{
    private readonly List<(
        Type Type,
        Type[] DependsOn,
        Func<IServiceProvider, T> Factory
    )> _entries = new();

    public override string Scheme => "IntoList<T>";

    public void Add(Type implementationType, Type[] dependsOn, Func<IServiceProvider, T> factory)
    {
        lock (Lock)
        {
            foreach (var entry in _entries)
            {
                if (entry.Type == implementationType)
                {
                    return;
                }
            }

            _entries.Add((implementationType, dependsOn, factory));
            Changed();
        }
    }

    protected override Func<IServiceProvider, T>[] Sort()
    {
        var names = _entries.Select(entry => GlueNames.Of(entry.Type)).ToArray();

        var order = GlueOrder.Sort(
            _entries.Count,
            (item, other) =>
            {
                foreach (var type in _entries[item].DependsOn)
                {
                    if (type.IsAssignableFrom(_entries[other].Type))
                    {
                        return true;
                    }
                }

                return false;
            },
            (x, y) => StringComparer.Ordinal.Compare(names[x], names[y]),
            names,
            typeof(IReadOnlyList<T>)
        );

        return order.Select(index => _entries[index].Factory).ToArray();
    }
}

internal sealed class GlueValueListBuilder<T, TOrder> : GlueListBuilder<T>
{
    private readonly List<(
        TOrder Value,
        TOrder[] DependsOn,
        Type Type,
        Func<IServiceProvider, T> Factory
    )> _entries = new();

    public override string Scheme => $"IntoList<T, {typeof(TOrder).Name}>";

    public void Add(
        TOrder value,
        TOrder[] dependsOn,
        Type implementationType,
        Func<IServiceProvider, T> factory
    )
    {
        lock (Lock)
        {
            var equality = EqualityComparer<TOrder>.Default;

            foreach (var entry in _entries)
            {
                if (entry.Type == implementationType && equality.Equals(entry.Value, value))
                {
                    return;
                }
            }

            _entries.Add((value, dependsOn, implementationType, factory));
            Changed();
        }
    }

    protected override Func<IServiceProvider, T>[] Sort()
    {
        var equality = EqualityComparer<TOrder>.Default;
        var comparer = GlueOrder.Comparer<TOrder>();
        var names = _entries.Select(entry => GlueNames.Of(entry.Type)).ToArray();

        var order = GlueOrder.Sort(
            _entries.Count,
            (item, other) =>
            {
                foreach (var value in _entries[item].DependsOn)
                {
                    if (equality.Equals(value, _entries[other].Value))
                    {
                        return true;
                    }
                }

                return false;
            },
            (x, y) =>
            {
                var byValue = comparer?.Compare(_entries[x].Value, _entries[y].Value) ?? 0;

                return byValue != 0 ? byValue : StringComparer.Ordinal.Compare(names[x], names[y]);
            },
            names,
            typeof(IReadOnlyList<T>)
        );

        return order.Select(index => _entries[index].Factory).ToArray();
    }
}

internal static class GlueOrder
{
    /// <summary>
    /// The comparer for order values and keys, or null when the type has no order.
    /// </summary>
    /// <remarks>
    /// Strings compare by ordinal, so the order does not change with the culture of the process.
    /// </remarks>
    public static IComparer<TValue>? Comparer<TValue>()
    {
        if (typeof(TValue) == typeof(string))
        {
            return (IComparer<TValue>)(object)StringComparer.Ordinal;
        }

        return
            typeof(IComparable<TValue>).IsAssignableFrom(typeof(TValue))
            || typeof(IComparable).IsAssignableFrom(typeof(TValue))
            ? System.Collections.Generic.Comparer<TValue>.Default
            : null;
    }

    /// <summary>
    /// Puts <paramref name="count"/> items in an order where each item is after the items it
    /// depends on. <paramref name="dependsOn"/> is true when its first item must come after its
    /// second. Items with no order between them are in the order of <paramref name="tieBreak"/>.
    /// </summary>
    public static int[] Sort(
        int count,
        Func<int, int, bool> dependsOn,
        Comparison<int> tieBreak,
        string[] names,
        Type listType
    )
    {
        var blockers = new int[count];
        var dependents = new List<int>?[count];

        for (var item = 0; item < count; item++)
        {
            for (var other = 0; other < count; other++)
            {
                if (item != other && dependsOn(item, other))
                {
                    blockers[item]++;
                    (dependents[other] ??= []).Add(item);
                }
            }
        }

        var ready = new List<int>();

        for (var item = 0; item < count; item++)
        {
            if (blockers[item] == 0)
            {
                ready.Add(item);
            }
        }

        var order = new int[count];
        var next = 0;

        while (ready.Count > 0)
        {
            ready.Sort(tieBreak);

            var item = ready[0];
            ready.RemoveAt(0);
            order[next++] = item;

            if (dependents[item] is not { } unblocked)
            {
                continue;
            }

            foreach (var dependent in unblocked)
            {
                if (--blockers[dependent] == 0)
                {
                    ready.Add(dependent);
                }
            }
        }

        if (next == count)
        {
            return order;
        }

        var stuck = Enumerable
            .Range(0, count)
            .Where(item => blockers[item] > 0)
            .Select(item => names[item])
            .OrderBy(name => name, StringComparer.Ordinal);

        throw new InvalidOperationException(
            $"The list {GlueNames.Of(listType)} has an order cycle. These items depend on each "
                + $"other, directly or through other items: {string.Join(", ", stuck)}."
        );
    }
}
