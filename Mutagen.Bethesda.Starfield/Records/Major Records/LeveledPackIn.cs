using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;

namespace Mutagen.Bethesda.Starfield;

public partial class LeveledPackIn
{
    [Flags]
    public enum MajorFlag
    {
        ApplyLrtToAllButPivot = 0x80
    }

    [Flags]
    public enum Flag
    {
        CalculateForEachItemInCount = 0x02,
        EvalAsStack = 0x20,
        DoAllBeforeRepeating = 0x100,
    }
}

partial class LeveledPackInBinaryCreateTranslation
{
    public static partial void FillBinaryChanceNoneCustom(MutagenFrame frame, ILeveledPackInInternal item, PreviousParse lastParsed)
    {
        var at = ValuePositionRecorder.Here(frame);
        var sub = frame.ReadSubrecord();
        item.ChanceNone = LeveledNpcBinaryCreateTranslation.GetChance(sub, frame.MetaData.FormVersion!.Value);
        // A byte (of 255) before form version FloatChanceNoneVersion, a float from it on
        if (at >= 0) ValuePositionRecorder.At(nameof(item.ChanceNone), at + sub.HeaderLength, sub.ContentLength, converted: sub.ContentLength == 1);
    }
}

partial class LeveledPackInBinaryWriteTranslation
{
    public static partial void WriteBinaryChanceNoneCustom(MutagenWriter writer, ILeveledPackInGetter item)
    {
        LeveledNpcBinaryWriteTranslation.WriteBinaryChanceNoneCustom(writer, item.ChanceNone, item.FormVersion);
    }
}

partial class LeveledPackInBinaryOverlay
{
    private float _chanceNone;
    
    partial void ChanceNoneCustomParse(OverlayStream stream, int finalPos, int offset)
    {
        _chanceNone = LeveledNpcBinaryCreateTranslation.GetChance(stream.ReadSubrecord(), this.FormVersion);
    }

    public partial float GetChanceNoneCustom()
    {
        return _chanceNone;
    }
}