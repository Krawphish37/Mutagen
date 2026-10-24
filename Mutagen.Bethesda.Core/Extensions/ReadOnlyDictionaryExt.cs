namespace Mutagen.Bethesda;

public static class ReadOnlyDictionaryExt
{
    /// <summary>
    /// Compares two dictionaries by their entries, regardless of the order the entries were added in.
    /// Two null dictionaries are equal; a null and a non-null dictionary are not.
    /// </summary>
    public static bool DictEqualsNullable<TKey, TValue>(
        this IReadOnlyDictionary<TKey, TValue>? lhs,
        IReadOnlyDictionary<TKey, TValue>? rhs)
    {
        if (ReferenceEquals(lhs, rhs)) return true;
        if (lhs == null || rhs == null) return false;
        if (lhs.Count != rhs.Count) return false;
        var comparer = EqualityComparer<TValue>.Default;
        foreach (var item in lhs)
        {
            if (!rhs.TryGetValue(item.Key, out var rhsValue)) return false;
            if (!comparer.Equals(item.Value, rhsValue)) return false;
        }
        return true;
    }
}
