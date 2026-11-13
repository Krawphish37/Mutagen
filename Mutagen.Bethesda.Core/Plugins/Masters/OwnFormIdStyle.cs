using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records.Internals;

namespace Mutagen.Bethesda.Plugins.Masters;

/// <summary>
/// How a Small or Medium plugin stores the FormIDs of the records it defines itself, for games with separated master
/// lists (Starfield): in its own style (FE + small index, FD + medium index) or as a full index (its position after
/// the full masters). Both occur: every official Small/Medium plugin uses its own style; mods do either (in one Data
/// folder, 153 Small and 15 Medium mods use a full index, 31 Small and 11 Medium their own style, 10 Small mix both).
/// Nothing in the file header tells them apart, so the record headers are read, and the result is reported
/// (RoundTripMixIn.GetOwnFormIdStyle). Writing doesn't follow it: a Small or Medium mod writes new own FormIDs in the
/// form of its size and keeps each stored one in its stored form (decision A3; RawHeaderFormIds, OwnFormIdForms: C147,
/// C163). Only a style set through RoundTripMixIn.SetOwnFormIdStyle decides the written form (a mod rebuilt from JSON).
/// </summary>
internal static class OwnFormIdStyle
{
    private sealed record Style(bool OwnStyle, bool Explicit);

    private static readonly ConditionalWeakTable<object, Style> Mods = new();

    /// <summary>
    /// Notes how a mod stores its own FormIDs (true: its own style; false: a full index; null: unknown).<br/>
    /// Read from its file (<paramref name="explicitly"/> false), it only reports the file's style: the mod writes new own
    /// FormIDs in the form of its size, and each stored one in its stored form (RawHeaderFormIds, OwnFormIdForms, C163).
    /// Set through RoundTripMixIn.SetOwnFormIdStyle (<paramref name="explicitly"/> true), it decides the form written
    /// for every own FormID: a mod rebuilt from another form (JSON) has no stored forms to keep.
    /// </summary>
    public static void Set(object mod, bool? ownStyle, bool explicitly = false)
    {
        if (ownStyle is { } s)
        {
            Mods.AddOrUpdate(mod, new Style(s, explicitly));
        }
        else
        {
            Mods.Remove(mod);
        }
    }

    public static bool? Get(object mod) => Mods.TryGetValue(mod, out var style) ? style.OwnStyle : null;

    /// <summary>
    /// The style set through RoundTripMixIn.SetOwnFormIdStyle, if any (not the one noted from the file)
    /// </summary>
    public static bool? GetExplicit(object mod) => Mods.TryGetValue(mod, out var style) && style.Explicit ? style.OwnStyle : null;

    /// <summary>
    /// A copy of a mod (DeepCopy) is written the way the mod it copies was read
    /// </summary>
    public static void Copy(object from, object to)
    {
        if (ReferenceEquals(from, to)) return;
        if (Mods.TryGetValue(from, out var style)) Mods.AddOrUpdate(to, style);
        else Mods.Remove(to);
    }

    /// <summary>
    /// Reads the record headers of a plugin and tells how it stores the FormIDs of its own records
    /// </summary>
    /// <param name="stream">The plugin, positioned anywhere; its position is restored</param>
    /// <param name="constants">Game constants</param>
    /// <param name="style">The plugin's own master style (from its header)</param>
    /// <param name="fullMasters">How many of its masters are Full</param>
    /// <param name="mediumMasters">How many are Medium</param>
    /// <param name="smallMasters">How many are Small</param>
    /// <returns>true: its own style; false: a full index; null: no own records, both, or a Full plugin</returns>
    public static bool? Detect(
        Stream stream,
        GameConstants constants,
        MasterStyle style,
        int fullMasters,
        int mediumMasters,
        int smallMasters)
    {
        if (style == MasterStyle.Full) return null;
        var start = stream.Position;
        try
        {
            long styled = 0, full = 0;
            var header = new byte[constants.MajorConstants.HeaderLength];
            var groupHeaderLength = constants.GroupConstants.HeaderLength;
            stream.Position = 0;
            var first = true;
            while (stream.Position + header.Length <= stream.Length)
            {
                var recordStart = stream.Position;
                if (stream.Read(header, 0, header.Length) != header.Length) break;
                var type = BinaryPrimitives.ReadUInt32LittleEndian(header);
                if (type == RecordTypes.GRUP.TypeInt)
                {
                    stream.Position = recordStart + groupHeaderLength;
                    continue;
                }
                var size = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
                stream.Position = recordStart + header.Length + size;
                if (first)
                {
                    // The mod header
                    first = false;
                    continue;
                }
                var id = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(constants.MajorConstants.FormIDLocationOffset));
                var formId = new FormID(id);
                switch (formId.FullMasterIndex)
                {
                    case FormID.SmallMasterMarker:
                        if (style == MasterStyle.Small && formId.LightMasterIndex == smallMasters) styled++;
                        break;
                    case FormID.MediumMasterMarker:
                        if (style == MasterStyle.Medium && formId.MediumMasterIndex == mediumMasters) styled++;
                        break;
                    default:
                        if (formId.FullMasterIndex == fullMasters) full++;
                        break;
                }
            }
            if (styled > 0 && full == 0) return true;
            if (full > 0 && styled == 0) return false;
            return null;
        }
        finally
        {
            stream.Position = start;
        }
    }
}
