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

    /// <summary> 
    /// Returns a FormKey with the next listed ID in the Mod's header. 
    /// No checks will be done that this is truly a unique key; It is assumed the header is in a correct state. 
    /// 
    /// The Mod's header will be incremented to mark the allocated key as "used". 
    /// </summary> 
    /// <returns>The next FormKey from the Mod</returns> 
    public FormKey GetNextFormKey()
    {
        lock (Mod)
        {
            var next = Mod.NextFormID;
            // A plugin read from a file can store a next object ID at or below its own records' highest one: the
            // Creation Kit's export of a small .esm stores 03000800 (full-index form) next to its own record 03000809,
            // and its plugins store FF001000 whatever their highest own ID. Counting up from there would hand out the ID
            // of a record the mod already has, and adding a record under that FormKey replaces it. So a read mod's new
            // IDs start above the highest own ID it was read with; the stored value (and its high byte) is otherwise
            // kept, so a mod with nothing added is written back as read.
            if (OverriddenFormsBaseline.GetSnapshot(Mod)?.MaxOwnId is { } maxOwnRead
                && (next & FormID.FullIdMask) <= maxOwnRead)
            {
                next = (next & ~FormID.FullIdMask) | checked(maxOwnRead + 1);
            }
            Mod.NextFormID = checked(next + 1);
            return new FormKey(Mod.ModKey, next);
        }
    }

    public FormKey GetNextFormKey(string? editorID)
    {
        return GetNextFormKey();
    }
}