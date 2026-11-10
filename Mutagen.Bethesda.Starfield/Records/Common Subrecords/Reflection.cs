using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;

namespace Mutagen.Bethesda.Starfield;

/// <summary>
/// A reflection subrecord (REFL, and PTCL, RDIF, PSDF, PCCC, XNSE where a record uses those signatures).<br/>
/// <see cref="IReflectionGetter.Data"/> holds the subrecord's exact bytes; <see cref="ReflectionMixIn.Parse"/> gives a
/// parsed view of them (<see cref="ReflectionData"/>).<br/>
/// <see cref="IReflectionGetter.FormLinks"/> are the FormIDs stored inside the data (pointers to forms), in order.
/// They are read as FormKeys and written back from the FormKeys with the writer's master list, so they take part in
/// link enumeration and remapping like any other link. When you replace <see cref="IReflectionGetter.Data"/>, set
/// FormLinks to match it, or clear them to write the data's own FormIDs unchanged.
/// </summary>
public partial class Reflection
{
}

public static partial class ReflectionMixIn
{
    /// <summary>A parsed view of the reflection data, or null if the bytes aren't a reflection blob.</summary>
    public static ReflectionData? Parse(this IReflectionGetter item) => ReflectionData.TryParse(item.Data);

    internal static IReadOnlyList<IFormLinkGetter<IStarfieldMajorRecordGetter>> ReadFormLinks(
        ReadOnlySpan<byte> data,
        IReadOnlySeparatedMasterPackage masters)
    {
        var locations = ReflectionData.FindFormIds(data);
        if (locations.Count == 0) return Array.Empty<IFormLinkGetter<IStarfieldMajorRecordGetter>>();
        var ret = new IFormLinkGetter<IStarfieldMajorRecordGetter>[locations.Count];
        for (int i = 0; i < ret.Length; i++)
        {
            ret[i] = new FormLink<IStarfieldMajorRecordGetter>(
                FormKeyBinaryTranslation.Instance.Parse(data.Slice(locations[i].Position, 4), masters));
        }
        return ret;
    }
}

partial class ReflectionBinaryCreateTranslation
{
    public static partial void CustomBinaryEndImport(MutagenFrame frame, IReflection obj)
    {
        obj.FormLinks.SetTo(ReflectionMixIn.ReadFormLinks(obj.Data, frame.MetaData.MasterReferences));
    }
}

partial class ReflectionBinaryWriteTranslation
{
    public static partial void CustomBinaryEndExport(MutagenWriter writer, IReflectionGetter obj)
    {
        var links = obj.FormLinks;
        if (links.Count == 0) return;
        var data = obj.Data;
        var locations = ReflectionData.FindFormIds(data);
        if (locations.Count != links.Count)
        {
            throw new ArgumentException(
                $"Reflection data holds {locations.Count} FormIDs but FormLinks has {links.Count}. "
                + "Set FormLinks to match Data, or clear them to write Data's own FormIDs.");
        }
        // Data has just been written, so its bytes end at the writer's position.
        // Each FormID in it is written again from its FormKey, with the master list of the plugin being written.
        var end = writer.Position;
        var start = end - data.Length;
        for (int i = 0; i < locations.Count; i++)
        {
            writer.Position = start + locations[i].Position;
            FormKeyBinaryTranslation.Instance.Write(writer, links[i]);
        }
        writer.Position = end;
    }
}

partial class ReflectionBinaryOverlay
{
    private IReadOnlyList<IFormLinkGetter<IStarfieldMajorRecordGetter>>? _formLinks;

    public IReadOnlyList<IFormLinkGetter<IStarfieldMajorRecordGetter>> FormLinks =>
        _formLinks ??= ReflectionMixIn.ReadFormLinks(Data, _package.MetaData.MasterReferences);
}
