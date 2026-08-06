using System.Collections;

namespace DependencyModules.SourceGenerator.Impl.Models;

/// <summary>
/// Structural equality helpers for the generator's model records.
///
/// These models feed Roslyn's incremental caching, so their equality decides how much work the
/// compiler redoes on each edit. A positional record compares its members with
/// EqualityComparer&lt;T&gt;.Default, which for IReadOnlyList members is reference equality — two
/// structurally identical models built on consecutive runs would never compare equal, and the
/// generator would regenerate everything on every keystroke.
/// </summary>
internal static class ModelEquality {

    public static bool ListEquals<T>(IReadOnlyList<T>? x, IReadOnlyList<T>? y) {
        if (ReferenceEquals(x, y)) {
            return true;
        }

        if (x is null || y is null || x.Count != y.Count) {
            return false;
        }

        for (var i = 0; i < x.Count; i++) {
            if (!EqualityComparer<T>.Default.Equals(x[i], y[i])) {
                return false;
            }
        }

        return true;
    }

    public static int ListHashCode<T>(IReadOnlyList<T>? list) {
        if (list is null) {
            return 0;
        }

        unchecked {
            var hash = 19;

            for (var i = 0; i < list.Count; i++) {
                hash = hash * 31 + (list[i]?.GetHashCode() ?? 0);
            }

            return hash;
        }
    }

    /// <summary>
    /// Compares attribute argument values, which arrive as object and may be arrays.
    /// </summary>
    public static bool ValueEquals(object? x, object? y) {
        if (Equals(x, y)) {
            return true;
        }

        if (x is string || y is string) {
            return false;
        }

        if (x is IEnumerable xs && y is IEnumerable ys) {
            var xEnumerator = xs.GetEnumerator();
            var yEnumerator = ys.GetEnumerator();

            while (true) {
                var xMoved = xEnumerator.MoveNext();
                var yMoved = yEnumerator.MoveNext();

                if (xMoved != yMoved) {
                    return false;
                }

                if (!xMoved) {
                    return true;
                }

                if (!ValueEquals(xEnumerator.Current, yEnumerator.Current)) {
                    return false;
                }
            }
        }

        return false;
    }

    public static int ValueHashCode(object? value) {
        if (value is null) {
            return 0;
        }

        if (value is string) {
            return value.GetHashCode();
        }

        if (value is IEnumerable enumerable) {
            unchecked {
                var hash = 19;

                foreach (var item in enumerable) {
                    hash = hash * 31 + ValueHashCode(item);
                }

                return hash;
            }
        }

        return value.GetHashCode();
    }
}
