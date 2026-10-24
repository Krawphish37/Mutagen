using System.Diagnostics.CodeAnalysis;
using Mutagen.Bethesda.Assets;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Assets;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;

namespace Mutagen.Bethesda;

public static class AssetLinkRemappingMixIn
{
    public static void Relink(this IAssetLink link, IReadOnlyDictionary<IAssetLinkGetter, string> mapping)
    {
        if (mapping.TryGetValue(link, out var replacement))
        {
            link.GivenPath = replacement;
        }
    }

    private static TLinkType RelinkToNew<TLinkType, TAssetType>(this TLinkType link, IReadOnlyDictionary<IAssetLinkGetter, string> mapping)
        where TLinkType : IAssetLink<TLinkType, TAssetType>, new()
        where TAssetType : IAssetType
    {
        if (mapping.TryGetValue(link, out var replacement))
        {
            var clone = new TLinkType();
            clone.GivenPath = replacement;
            return clone;
        }
        return link;
    }

    /// <summary>
    /// Returns a new AssetLink pointing to the mapped path, or the given link if it is not mapped.<br />
    /// The given link is never modified, so it stays valid as a key of the mapping
    /// (which may have been built from a record's own links) while other links are looked up.
    /// </summary>
    public static AssetLink<TAssetType> RelinkToNew<TAssetType>(this AssetLink<TAssetType> link, IReadOnlyDictionary<IAssetLinkGetter, string> mapping)
        where TAssetType : class, IAssetType
    {
        if (mapping.TryGetValue(link, out var replacement))
        {
            var clone = new AssetLink<TAssetType>();
            clone.GivenPath = replacement;
            return clone;
        }
        return link;
    }

    [return: NotNullIfNotNull(nameof(link))]
    private static IAssetLink<TAssetType>? RelinkToNew<TAssetType>(this IAssetLink<TAssetType>? link, IReadOnlyDictionary<IAssetLinkGetter, string> mapping)
        where TAssetType : class, IAssetType
    {
        if (link != null && mapping.TryGetValue(link, out var replacement))
        {
            var clone = new AssetLink<TAssetType>();
            clone.GivenPath = replacement;
            return clone;
        }
        return link;
    }

    public static void RemapListedAssetLinks<TAssetType>(this IList<IAssetLink<TAssetType>> linkList, IReadOnlyDictionary<IAssetLinkGetter, string> mapping)
        where TAssetType : class, IAssetType
    {
        for (int i = 0; i < linkList.Count; i++)
        {
            linkList[i] = linkList[i].RelinkToNew(mapping);
        }
    }

    public static void RemapListedAssetLinks<TAssetType>(this IAssetLink<TAssetType>?[] linkArray, IReadOnlyDictionary<IAssetLinkGetter, string> mapping)
        where TAssetType : class, IAssetType
    {
        for (int i = 0; i < linkArray.Length; i++)
        {
            linkArray[i] = linkArray[i].RelinkToNew(mapping);
        }
    }

    public static void RemapListedAssetLinks<TLinkType, TAssetType>(this IList<TLinkType> linkList, IReadOnlyDictionary<IAssetLinkGetter, string> mapping)
        where TLinkType : IAssetLink<TLinkType, TAssetType>, new()
        where TAssetType : IAssetType
    {
        for (int i = 0; i < linkList.Count; i++)
        {
            linkList[i] = linkList[i].RelinkToNew<TLinkType, TAssetType>(mapping);
        }
    }
    
    public static void RemapAssetLinks<TItem>(this IList<IAssetLinkContainer> linkList, IReadOnlyDictionary<IAssetLinkGetter, string> mapping, AssetLinkQuery query, IAssetLinkCache? linkCache)
    {
        foreach (var item in linkList)
        {
            item.RemapAssetLinks(mapping, query, linkCache);
        }
    }
    
    public static void RemapAssetLinks<TItem>(this IGenderedItemGetter<TItem?> gendered, IReadOnlyDictionary<IAssetLinkGetter, string> mapping, AssetLinkQuery query, IAssetLinkCache? linkCache)
        where TItem : class, IAssetLinkContainer
    {
        gendered.Male?.RemapAssetLinks(mapping, query, linkCache);
        gendered.Female?.RemapAssetLinks(mapping, query, linkCache);
    }

    public static void RemapAssetLinks<TLinkType, TAssetType>(this IGenderedItem<TLinkType> gendered, IReadOnlyDictionary<IAssetLinkGetter, string> mapping)
        where TLinkType : IAssetLink<TLinkType, TAssetType>, new()
        where TAssetType : IAssetType
    {
        gendered.Male = gendered.Male.RelinkToNew<TLinkType, TAssetType>(mapping);
        gendered.Female = gendered.Female.RelinkToNew<TLinkType, TAssetType>(mapping);
    }

    public static void RemapAssetLinks<TMajorGetter>(this IReadOnlyCache<TMajorGetter, FormKey> cache, IReadOnlyDictionary<IAssetLinkGetter, string> mapping, AssetLinkQuery query, IAssetLinkCache? linkCache)
        where TMajorGetter : class, IMajorRecordGetter, IAssetLinkContainer
    {
        foreach (var item in cache.Items)
        {
            item.RemapAssetLinks(mapping, query, linkCache);
        }
    }
}