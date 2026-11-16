namespace Mutagen.Bethesda.Plugins.Binary;

/// <summary>
/// How <see cref="MajorRecordReader"/> reads the children of a record that holds others (a cell's references, a
/// worldspace's cells, a dialog topic's responses, a quest's topics): the group that follows it in the file, labelled
/// with its FormID (C58/W9)
/// </summary>
public enum ChildRecordRead
{
    /// <summary>
    /// The record alone: its children lists are empty and its group values (timestamps, ...) are defaults
    /// </summary>
    None,

    /// <summary>
    /// The record, and its children group laid out as the full import lays it out (which list each child is in, their
    /// order, the sub-groups and the group values), each child read from its record header alone: its FormKey, type,
    /// header flags and versions, with no field read (as if it held no subrecord). The children of the children (a
    /// worldspace's cells' references) are left out. A child read this way is for looking at, not for writing back.
    /// </summary>
    Headers,

    /// <summary>
    /// The record with its whole children group, every child read in full, as a full import reads it
    /// </summary>
    Full,
}
