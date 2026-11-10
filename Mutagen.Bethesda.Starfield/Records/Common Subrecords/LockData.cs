namespace Mutagen.Bethesda.Starfield;

partial class LockData
{
    [Flags]
    public enum Flag
    {
        // xEdit "Unknown 0". In Starfield.esm it is set on 1,436 of 1,958 locks, 1,419 of them with a lock level
        // (including Requires Key, Inaccessible and Requires Terminal, which can't be leveled); 485 of the 498 locks
        // without it have level None. It looks like "Locked", but nothing names it.
        Unknown0 = 0x01,
        // xEdit "Leveled Lock" (bit 2): 24 locks in Starfield.esm
        LeveledLock = 0x04,
    }
}