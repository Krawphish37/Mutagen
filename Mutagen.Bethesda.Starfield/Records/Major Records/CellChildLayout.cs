using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Mutagen.Bethesda.Starfield;

/// <summary>
/// Keeps the layout of a cell's children group as it was read (C137, C138): whether the temporary children group came
/// before the persistent one, and where the navmeshes sat among the temporary children. Mutagen writes persistent
/// first and navmeshes first; the Creation Kit doesn't always (Starfield.esm: 1,511 cells with the temporary group
/// first; darkstar.esm: 96 cells with the navmesh after the placed objects).<br/>
/// Only a layout that differs from Mutagen's is noted. It is used on write while the cell still holds as many
/// navmeshes and temporary children as it was read with; otherwise Mutagen's layout is written. A copy (DeepCopy,
/// DeepCopyIn) takes it over. It doesn't take part in Equals.
/// </summary>
internal static class CellChildLayout
{
    /// <param name="TemporaryFirst">The temporary children group came before the persistent one</param>
    /// <param name="Runs">The temporary group's records as alternating runs: navmeshes, temporaries, navmeshes, ...</param>
    internal sealed record Layout(bool TemporaryFirst, int[] Runs)
    {
        public bool Fits(int navmeshes, int temporaries)
        {
            int n = 0, t = 0;
            for (int i = 0; i < Runs.Length; i++)
            {
                if (i % 2 == 0) n += Runs[i];
                else t += Runs[i];
            }
            return n == navmeshes && t == temporaries;
        }
    }

    private static readonly ConditionalWeakTable<object, Layout> Layouts = new();

    /// <summary>
    /// Notes a cell's layout as read, if it differs from Mutagen's
    /// </summary>
    /// <param name="temporaryPattern">The temporary group's records in read order: true for a navmesh</param>
    internal static void Remember(object cell, bool temporaryFirst, IReadOnlyList<bool> temporaryPattern)
    {
        var runs = new List<int> { 0 };
        foreach (var isNavmesh in temporaryPattern)
        {
            var wantNavmesh = runs.Count % 2 == 1;
            if (isNavmesh != wantNavmesh) runs.Add(0);
            runs[^1]++;
        }
        // Mutagen's own layout: persistent first, and at most one run of navmeshes followed by one of temporaries
        if (!temporaryFirst && runs.Count <= 2)
        {
            Layouts.Remove(cell);
            return;
        }
        Layouts.AddOrUpdate(cell, new Layout(temporaryFirst, runs.ToArray()));
    }

    internal static bool TryGet(object cell, out Layout layout) => Layouts.TryGetValue(cell, out layout!);

    internal static void Copy(object from, object to)
    {
        if (ReferenceEquals(from, to)) return;
        if (Layouts.TryGetValue(from, out var layout)) Layouts.AddOrUpdate(to, layout);
        else Layouts.Remove(to);
    }

    private const string Prefix = "CL1:";

    internal static string? Export(object cell)
    {
        if (!Layouts.TryGetValue(cell, out var layout)) return null;
        var bytes = new byte[1 + 4 * layout.Runs.Length];
        bytes[0] = layout.TemporaryFirst ? (byte)1 : (byte)0;
        for (int i = 0; i < layout.Runs.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(1 + 4 * i), layout.Runs[i]);
        }
        return Prefix + Convert.ToBase64String(bytes);
    }

    internal static void Import(object cell, string? value)
    {
        if (value == null)
        {
            Layouts.Remove(cell);
            return;
        }
        if (!value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new FormatException($"Not a cell child-group layout: {value}");
        }
        var bytes = Convert.FromBase64String(value.Substring(Prefix.Length));
        if (bytes.Length < 1 || (bytes.Length - 1) % 4 != 0 || bytes[0] > 1)
        {
            throw new FormatException($"Not a cell child-group layout: {value}");
        }
        var runs = new int[(bytes.Length - 1) / 4];
        for (int i = 0; i < runs.Length; i++)
        {
            runs[i] = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(1 + 4 * i));
            if (runs[i] < 0) throw new FormatException($"Not a cell child-group layout: {value}");
        }
        Layouts.AddOrUpdate(cell, new Layout(bytes[0] == 1, runs));
    }
}

public static partial class CellMixIn
{
    /// <summary>
    /// The layout this cell's children group was read with, when it differs from the one Mutagen writes (which group
    /// comes first, and where navmeshes sit among the temporary children): an opaque, versioned string, or null.
    /// For tools that rebuild a cell from another form (JSON, a database) and want it written back as it was.
    /// </summary>
    public static string? GetChildGroupLayout(this ICellGetter cell) => CellChildLayout.Export(cell);

    /// <summary>
    /// Sets the layout to write this cell's children group with (a value from <see cref="GetChildGroupLayout"/>);
    /// null removes it. It is used only while the cell holds as many navmeshes and temporary children as the layout
    /// describes; otherwise Mutagen's layout is written.
    /// </summary>
    /// <exception cref="FormatException">The value is not one GetChildGroupLayout returns</exception>
    public static void SetChildGroupLayout(this ICell cell, string? layout) => CellChildLayout.Import(cell, layout);
}

internal partial class CellSetterTranslationCommon
{
    // A copy takes over the child-group layout the copied cell was read with (see CellChildLayout)
    partial void DeepCopyInCustom(
        ICell item,
        ICellGetter rhs,
        global::Loqui.Internal.ErrorMaskBuilder? errorMask,
        global::Loqui.Internal.TranslationCrystal? copyMask,
        bool deepCopy)
    {
        CellChildLayout.Copy(rhs, item);
    }
}
