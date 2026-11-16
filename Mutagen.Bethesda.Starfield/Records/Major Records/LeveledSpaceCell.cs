using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Starfield.Internals;

namespace Mutagen.Bethesda.Starfield;

public partial class LeveledSpaceCell
{
    [Flags]
    public enum Flag
    {
        DoAllBeforeRepeating = 0x100,
    }
}

partial class LeveledSpaceCellBinaryCreateTranslation
{
    public const int FloatChanceNoneVersion = 510;

    public static float GetChance(SubrecordFrame subrecord, ushort version)
    {
        if (version < FloatChanceNoneVersion)
        {
            var chance = subrecord.AsUInt8();
            return chance / 255f;
        }
        else
        {
            return subrecord.AsFloat();
        }
    }
    
    public static partial void FillBinaryChanceNoneCustom(MutagenFrame frame, ILeveledSpaceCellInternal item, PreviousParse lastParsed)
    {
        var at = ValuePositionRecorder.Here(frame);
        var sub = frame.ReadSubrecord();
        item.ChanceNone = GetChance(sub, frame.MetaData.FormVersion!.Value);
        // A byte (of 255) before form version FloatChanceNoneVersion, a float from it on
        if (at >= 0) ValuePositionRecorder.At(nameof(item.ChanceNone), at + sub.HeaderLength, sub.ContentLength, converted: sub.ContentLength == 1);
    }
}

partial class LeveledSpaceCellBinaryWriteTranslation
{
    public static void WriteBinaryChanceNoneCustom(MutagenWriter writer, float chanceNone, ushort formVersion)
    {
        using var s = HeaderExport.Subrecord(writer, RecordTypes.LVLD);
        if (formVersion < LeveledSpaceCellBinaryCreateTranslation.FloatChanceNoneVersion)
        {
            writer.Write((byte)Math.Round(chanceNone * 255));
        }
        else
        {
            writer.Write(chanceNone);
        }
    }
    
    public static partial void WriteBinaryChanceNoneCustom(MutagenWriter writer, ILeveledSpaceCellGetter item)
    {
        WriteBinaryChanceNoneCustom(writer, item.ChanceNone, item.FormVersion);
    }
}

partial class LeveledSpaceCellBinaryOverlay
{
    private float _chanceNone;
    
    partial void ChanceNoneCustomParse(OverlayStream stream, int finalPos, int offset)
    {
        _chanceNone = LeveledSpaceCellBinaryCreateTranslation.GetChance(stream.ReadSubrecord(), this.FormVersion);
    }

    public partial float GetChanceNoneCustom()
    {
        return _chanceNone;
    }
}