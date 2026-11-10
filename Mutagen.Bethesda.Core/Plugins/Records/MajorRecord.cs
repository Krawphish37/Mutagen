using Noggog;
using System.Diagnostics;
using Mutagen.Bethesda.Assets;
using Mutagen.Bethesda.Plugins.Assets;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Internals;
using Mutagen.Bethesda.Plugins.Masters;

namespace Mutagen.Bethesda.Plugins.Records;

public partial interface IMajorRecord : IFormLinkContainer, IAssetLinkContainer, IMajorRecordQueryable
{
    new FormKey FormKey { get; }
        
    /// <summary>
    /// Marker of whether the content is compressed
    /// </summary>
    new bool IsCompressed { get; set; }

    /// <summary>
    /// Marker of whether the content is deleted
    /// </summary>
    new bool IsDeleted { get; set; }

    /// <summary>
    /// Disables the record by setting the RecordFlag to Initially Disabled.
    /// <returns>Returns true if the disable was successful.</returns>
    /// </summary>
    bool Disable();
}
    
public partial interface IMajorRecordInternal
{
    new FormKey FormKey { get; set; }
}

public partial interface IMajorRecordGetter : 
    IFormVersionGetter, 
    IMajorRecordIdentifierGetter,
    IFormLinkContainerGetter,
    IAssetLinkContainerGetter,
    IFormLinkIdentifier,
    IEquatable<IFormLinkGetter>,
    IMajorRecordQueryableGetter
{
    /// <summary>
    /// Marker of whether the content is compressed
    /// </summary>
    bool IsCompressed { get; }

    /// <summary>
    /// Marker of whether the content is deleted
    /// </summary>
    bool IsDeleted { get; }

    /// <summary>
    /// Form Version of the record
    /// </summary>
    new ushort? FormVersion { get; }
}

[DebuggerDisplay("{GetType().Name} {this.EditorID?.ToString()} {this.FormKey.ToString()}")]
public partial class MajorRecord : IFormLinkContainer
{
    #region EditorID
    public virtual String? EditorID { get; set; }
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    String? IMajorRecordGetter.EditorID => EditorID;
    #endregion

    /// <summary>
    /// A convenience property to print "EditorID - FormKey"
    /// </summary>
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public string TitleString => $"{EditorID} - {FormKey}";

    public bool IsCompressed
    {
        get => Enums.HasFlag(MajorRecordFlagsRaw, Constants.CompressedFlag);
        set => MajorRecordFlagsRaw = Enums.SetFlag(MajorRecordFlagsRaw, Constants.CompressedFlag, value);
    }

    public bool IsDeleted
    {
        get => Enums.HasFlag(MajorRecordFlagsRaw, Constants.DeletedFlag);
        set => MajorRecordFlagsRaw = Enums.SetFlag(MajorRecordFlagsRaw, Constants.DeletedFlag, value);
    }

    protected abstract ushort? FormVersionAbstract { get; }
    ushort? IMajorRecordGetter.FormVersion => FormVersionAbstract;
    ushort? IFormVersionGetter.FormVersion => FormVersionAbstract;

    public virtual bool Disable()
    {
        if (IsDeleted) return false;
        MajorRecordFlagsRaw = Enums.SetFlag(MajorRecordFlagsRaw, (int)Constants.InitiallyDisabled, true);
        return true;
    }

    #region Comparers
    public static IEqualityComparer<IMajorRecordGetter> FormKeyEqualityComparer => _formKeyEqualityComparer;

    private static readonly MajorRecordFormKeyComparer _formKeyEqualityComparer = new();

    class MajorRecordFormKeyComparer : IEqualityComparer<IMajorRecordGetter>
    {
        public bool Equals(IMajorRecordGetter? x, IMajorRecordGetter? y)
        {
            return x?.FormKey == y?.FormKey;
        }

        public int GetHashCode(IMajorRecordGetter obj)
        {
            return obj.FormKey.GetHashCode();
        }
    }
    #endregion

    public bool Equals(IFormLinkGetter? other)
    {
        if (other == null) return false;
        return other.Equals(this);
    }

    Type ILinkIdentifier.Type => LinkType;
    protected abstract Type LinkType { get; }
}

public static class IMajorRecordGetterExt
{
    public static FormLinkInformation ToFormLinkInformation(this IMajorRecordGetter majorRec)
    {
        return FormLinkInformation.Factory(majorRec);
    }
    
    public static bool IsInjected(this IMajorRecordGetter majorRec, ILinkCache linkCache)
    {
        return !linkCache.TryResolveSimpleContext(majorRec, out var context, target: ResolveTarget.Origin)
            || context.ModKey != majorRec.FormKey.ModKey;
    }
}
    
/// <summary>
/// Subrecords a record holds but no field claims. They're kept as raw bytes and written back at the end of the record.
/// FormIDs inside them can't be corrected, so they're only written to a plugin with the same name and master list they came from.
/// </summary>
public static class MajorRecordUnknownSubrecords
{
    /// <summary>
    /// Called when a record's unknown subrecords can't be written, because the plugin being saved has other masters.
    /// </summary>
    public static Action<string> NotWritten { get; set; } = message => Console.Error.WriteLine(message);

    internal static string SourceKey(IReadOnlyMasterReferenceCollection masters)
    {
        return masters.CurrentMod + "|" + string.Join("|", masters.Masters.Select(x => x.Master.ToString()));
    }

    internal static byte[] Append(byte[]? existing, ReadOnlySpan<byte> subrecord)
    {
        var ret = new byte[(existing?.Length ?? 0) + subrecord.Length];
        existing?.CopyTo(ret, 0);
        subrecord.CopyTo(ret.AsSpan(existing?.Length ?? 0));
        return ret;
    }

    internal static void Keep(IMajorRecordInternal item, MutagenFrame frame, int contentLength)
    {
        var total = frame.MetaData.Constants.SubConstants.HeaderLength + contentLength;
        var pos = frame.Position;
        var memory = frame.ReadMemory(total);
        frame.Position = pos;
        item.UnknownSubrecords = Append(item.UnknownSubrecords?.ToArray(), memory);
        item.UnknownSubrecordsSource = SourceKey(frame.MetaData.MasterReferences.Raw);
    }

    internal static void Write(IMajorRecordGetter item, MutagenWriter writer, MutagenWriter target)
    {
        if (item.UnknownSubrecords is not { } bytes) return;
        var masters = writer.MetaData.MasterReferences;
        if (masters != null && item.UnknownSubrecordsSource != SourceKey(masters))
        {
            NotWritten($"{item.FormKey} {item.EditorID}: {bytes.Length} bytes of unknown subrecords were not written, because this plugin's masters differ from {item.UnknownSubrecordsSource}");
            return;
        }
        target.Write(bytes);
    }
}

[DebuggerDisplay("{GetType().Name} {this.EditorID?.ToString()} {this.FormKey.ToString()}")]
internal abstract partial class MajorRecordBinaryOverlay : IMajorRecordGetter
{
    public bool IsCompressed => Enums.HasFlag(MajorRecordFlagsRaw, Constants.CompressedFlag);
    public bool IsDeleted => Enums.HasFlag(MajorRecordFlagsRaw, Constants.DeletedFlag);

    public ReadOnlyMemorySlice<byte>? UnknownSubrecords => _unknownSubrecords;
    private byte[]? _unknownSubrecords;
    public string? UnknownSubrecordsSource { get; private set; }

    /// <summary>
    /// Keeps a subrecord no field claimed, with its header. The stream stays where it is.
    /// </summary>
    protected void KeepUnknownSubrecord(OverlayStream stream)
    {
        var header = stream.GetSubrecordHeader();
        _unknownSubrecords = MajorRecordUnknownSubrecords.Append(_unknownSubrecords, stream.RemainingMemory.Slice(0, header.TotalLength));
        UnknownSubrecordsSource = MajorRecordUnknownSubrecords.SourceKey(_package.MetaData.MasterReferences.Raw);
    }

    protected abstract ushort? FormVersionAbstract { get; }
    ushort? IMajorRecordGetter.FormVersion => FormVersionAbstract;
    ushort? IFormVersionGetter.FormVersion => FormVersionAbstract;

    Type ILinkIdentifier.Type => LinkType;
    protected abstract Type LinkType { get; }

    public bool Equals(IFormLinkGetter? other)
    {
        if (other == null) return false;
        return other.Equals(this);
    }
}

internal partial class MajorRecordSetterTranslationCommon
{
    // A copy takes over the subrecord order the copied record was read with (see SubrecordOrder; only Starfield notes
    // any). It is used on write only while the copy still holds the same subrecords.
    partial void DeepCopyInCustom(
        IMajorRecord item,
        IMajorRecordGetter rhs,
        global::Loqui.Internal.ErrorMaskBuilder? errorMask,
        global::Loqui.Internal.TranslationCrystal? copyMask,
        bool deepCopy)
    {
        Mutagen.Bethesda.Plugins.Binary.Translations.SubrecordOrder.Copy(rhs, item);
    }
}