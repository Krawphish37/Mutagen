namespace Mutagen.Bethesda.Plugins.Records.Mapping;

/// <summary>
/// A collection that can enumerate its items whose type is assignable to a given type
/// without constructing the items that are not, such as an overlay list that
/// knows each item's type from its record header
/// </summary>
internal interface IAssignableTypeEnumerable<out T>
{
    /// <summary>
    /// Enumerates the items whose runtime type is assignable to the given type, in order
    /// </summary>
    IEnumerable<T> WhereAssignableTo(Type type);
}

internal static class AssignableTypeEnumerableExt
{
    /// <summary>
    /// Enumerates the items whose runtime type is assignable to the given type, in order. <br/>
    /// Collections implementing <see cref="IAssignableTypeEnumerable{T}"/> can skip the other items
    /// without constructing them.
    /// </summary>
    public static IEnumerable<T> WhereAssignableTo<T>(this IEnumerable<T> items, Type type)
        where T : notnull
    {
        if (items is IAssignableTypeEnumerable<T> assignable)
        {
            return assignable.WhereAssignableTo(type);
        }
        return WhereAssignableToByInstance(items, type);
    }

    private static IEnumerable<T> WhereAssignableToByInstance<T>(IEnumerable<T> items, Type type)
        where T : notnull
    {
        foreach (var item in items)
        {
            if (type.IsAssignableFrom(item.GetType()))
            {
                yield return item;
            }
        }
    }
}
