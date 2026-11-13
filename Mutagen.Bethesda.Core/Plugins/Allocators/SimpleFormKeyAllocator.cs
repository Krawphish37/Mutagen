using Mutagen.Bethesda.Plugins.Records;

namespace Mutagen.Bethesda.Plugins.Allocators;

/// <summary> 
/// A simple FormKey allocator that simply leverages a Mod's NextObjectID tracker to allocate. 
/// No safety checks or syncronization is provided. 
/// 
/// This class is thread safe. 
/// </summary> 
public sealed class SimpleFormKeyAllocator : IFormKeyAllocator
{
    /// <summary> 
    /// Attached Mod that will be used as reference when allocating new keys 
    /// </summary> 
    public IMod Mod { get; }

    /// <summary> 
    /// Constructs a new SimpleNextIDAllocator that looks to a given Mod for the next key 
    /// </summary> 
    public SimpleFormKeyAllocator(IMod mod)
    {
        Mod = mod;
    }

    // The highest object ID among the mod's own records: walked once at the first allocation (every record, nested ones
    // included), then raised by each allocation and by own records added to a top-level group (AGroup tells the mod).
    // Null until the first allocation; -1 when the mod has no own records.
    private long? _maxOwn;

    /// <summary>
    /// Notes an own record added to the mod, so an ID it uses is never handed out
    /// </summary>
    internal void NoteOwnId(uint id)
    {
        lock (Mod)
        {
            if (_maxOwn is { } known && id > known) _maxOwn = id;
        }
    }

    private long WalkMaxOwn()
    {
        long max = -1;
        foreach (var rec in Mod.EnumerateMajorRecords())
        {
            if (rec.FormKey.ModKey == Mod.ModKey && rec.FormKey.ID > max) max = rec.FormKey.ID;
        }
        return max;
    }

    /// <summary> 
    /// Returns a FormKey with the next listed ID in the Mod's header, moved up past any object ID the mod's own records
    /// already use, and kept within the mod's size (a Small master's object IDs end at 0xFFF, a Medium master's at
    /// 0xFFFF).
    /// 
    /// The Mod's header will be incremented to mark the allocated key as "used". 
    /// </summary> 
    /// <returns>The next FormKey from the Mod</returns> 
    /// <exception cref="InvalidOperationException">A Small or Medium master whose object-ID range is used up</exception>
    public FormKey GetNextFormKey()
    {
        lock (Mod)
        {
            // A plugin's stored next object ID can be at or below its own records' highest one: the Creation Kit's export
            // of a small .esm stores 03000800 (full-index form) next to its own record 03000809, its plugins store
            // FF001000 whatever their highest own ID, and records can be added with explicit FormKeys. Counting up from
            // the stored value would hand out the ID of a record the mod already has, and adding a record under that
            // FormKey replaces it. The stored high byte is kept; a mod with nothing added is written back as read.
            _maxOwn ??= WalkMaxOwn();
            var next = Mod.NextFormID;
            long id = next & FormID.FullIdMask;
            if (id <= _maxOwn.Value) id = _maxOwn.Value + 1;
            var limit = Mod.GetMasterStyle() switch
            {
                MasterStyle.Small => FormID.SmallIdMask,
                MasterStyle.Medium => FormID.MediumIdMask,
                _ => FormID.FullIdMask,
            };
            if (id > limit)
            {
                // A stored value past the size's range (a Small plugin storing the CK's FF001000): start after the
                // highest own ID, or at 0x800 when there is none
                id = _maxOwn.Value >= 0 ? _maxOwn.Value + 1 : 0x800;
                if (id > limit)
                {
                    throw new InvalidOperationException(
                        $"{Mod.ModKey} is a {Mod.GetMasterStyle()} master and its object IDs are used up to 0x{_maxOwn.Value:X}: " +
                        $"no new FormKey fits its range (up to 0x{limit:X}). Write it as a larger master to add more records.");
                }
            }
            var formId = (next & ~FormID.FullIdMask) | (uint)id;
            Mod.NextFormID = checked(formId + 1);
            _maxOwn = id;
            return new FormKey(Mod.ModKey, formId);
        }
    }

    public FormKey GetNextFormKey(string? editorID)
    {
        return GetNextFormKey();
    }
}