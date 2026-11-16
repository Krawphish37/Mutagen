using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Loqui;
using Loqui.Internal;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Internals;
using Constants = Mutagen.Bethesda.Plugins.Internals.Constants;

namespace Mutagen.Bethesda.Plugins.Records;

/// <summary>
/// Partial forms (C58/W7): a record whose header has the Partial Form flag (0x4000) holds only some of its fields. The
/// game lays the fields it holds over the version of the record before it (in the plugin's masters) and keeps that
/// version's values for the rest. Only some record types can be partial forms: those marked partialForm="true" in
/// Mutagen's XML (IsPartialFormable on their registration), as xEdit's definitions list them. In Starfield: Cell,
/// DialogTopic, Location, Quest and Worldspace.<br/>
/// Mutagen reads a partial form as it is written: a field it doesn't hold is absent, or its default. These tell which
/// fields a record's bytes held, and make the record the game has after a partial form.
/// </summary>
public static class PartialFormMixIn
{
    /// <summary>
    /// The record header flag that marks a partial form (bit 14)
    /// </summary>
    public const int PartialFormFlag = Constants.PartialFormFlag;

    private static readonly ConcurrentDictionary<Type, bool> Formable = new();

    /// <summary>
    /// Whether records of a type can be partial forms: its XML marks it partialForm="true" (its registration's
    /// IsPartialFormable). On other types the 0x4000 header flag means something else, or nothing.
    /// </summary>
    /// <param name="recordType">A major record class or interface</param>
    public static bool CanBePartialForm(Type recordType)
    {
        return Formable.GetOrAdd(recordType, static t =>
            LoquiRegistration.TryGetRegister(t, out var registration) && CanBePartialForm(registration));
    }

    /// <summary>
    /// Whether records of the record's type can be partial forms (see <see cref="CanBePartialForm(Type)"/>)
    /// </summary>
    public static bool CanBePartialForm(this IMajorRecordGetter record) => CanBePartialForm(record.Registration);

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "The record classes are registered with Loqui and their registrations' properties are public")]
    private static bool CanBePartialForm(ILoquiRegistration registration)
    {
        return Formable.GetOrAdd(registration.ClassType, static (_, r) =>
            r.GetType().GetProperty(Constants.PartialFormMember, BindingFlags.Static | BindingFlags.Public)
                ?.GetValue(null) is true, registration);
    }

    /// <summary>
    /// Whether the record is a partial form: its type can be one, and its header has the Partial Form flag (0x4000)
    /// </summary>
    public static bool IsPartialForm(this IMajorRecordGetter record)
    {
        return (record.MajorRecordFlagsRaw & PartialFormFlag) != 0 && record.CanBePartialForm();
    }

    /// <summary>
    /// The fields the record's bytes held: the index (in its type's FieldIndex enum, e.g. Location_FieldIndex) of each
    /// field Mutagen's reader read from one of the record's subrecords, in ascending order. A field that isn't listed
    /// had no subrecord: it reads as absent (null) or as its default. The header's values (flags, FormKey, versions)
    /// and the records of its child groups come from no subrecord of its own, so they are never listed.<br/>
    /// Known for:
    /// <list type="bullet">
    /// <item>a record from a binary overlay: worked out from its bytes, by the full import's reader, when asked;</item>
    /// <item>a record from a full import (or <see cref="Mutagen.Bethesda.Plugins.Binary.MajorRecordReader"/>) of a type
    /// that can be a partial form (<see cref="CanBePartialForm(Type)"/>): noted while it was read;</item>
    /// <item>a record made by <see cref="MergeOnto"/>.</item>
    /// </list>
    /// Not known for other records (other full-import types, records made or copied in code): false.<br/>
    /// A subrecord read into several fields of the record itself (a struct whose members are fields of the record, with
    /// a DataType marker) is listed by the reader's own report: the last of those fields it read. No type that can be a
    /// partial form has such a subrecord.
    /// </summary>
    /// <param name="record">The record</param>
    /// <param name="fieldIndices">The indices, when known</param>
    /// <returns>Whether the fields are known</returns>
    public static bool TryGetPresentFields(this IMajorRecordGetter record, [NotNullWhen(true)] out IReadOnlyList<int>? fieldIndices)
    {
        if (PresentFields.TryGet(record, out var found))
        {
            fieldIndices = found;
            return true;
        }
        if (record is PluginBinaryOverlay overlay && PresentFields.FromOverlay(overlay, record) is { } computed)
        {
            fieldIndices = computed;
            return true;
        }
        fieldIndices = null;
        return false;
    }

    /// <summary>
    /// The record as the game has it after a partial form: the version before it (<paramref name="baseVersion"/>), with
    /// each field the partial form's bytes hold laid over it. Header values (flags, form version, version control) and
    /// the records of its child groups (a cell's references, a quest's topics, ...) are the partial form's: they aren't
    /// fields of its subrecords, and child records are records of their own. The result's flags lose the Partial Form
    /// flag, as it is whole. A record that isn't a partial form is given back as a copy of itself.<br/>
    /// When the version before is a partial form too, merge it over its own base first: the merged record knows its
    /// fields (<see cref="TryGetPresentFields"/>).
    /// </summary>
    /// <param name="partialForm">The partial form (or any record)</param>
    /// <param name="baseVersion">The same record (FormKey and type) as the plugin's masters have it, the last of them
    /// that holds it</param>
    /// <returns>A new record</returns>
    /// <exception cref="ArgumentException">The two aren't the same record</exception>
    /// <exception cref="InvalidOperationException">Which fields a record held isn't known (see
    /// <see cref="TryGetPresentFields"/>)</exception>
    public static MajorRecord MergeOnto(this IMajorRecordGetter partialForm, IMajorRecordGetter baseVersion)
    {
        if (partialForm.FormKey != baseVersion.FormKey)
        {
            throw new ArgumentException($"A partial form is merged onto the same record: {partialForm.FormKey} is not {baseVersion.FormKey}");
        }
        if (partialForm.Registration.ClassType != baseVersion.Registration.ClassType)
        {
            throw new ArgumentException($"A partial form is merged onto the same record: {partialForm.FormKey} is a {partialForm.Registration.ClassType.Name}, its base a {baseVersion.Registration.ClassType.Name}");
        }
        var merged = partialForm.DeepCopy();
        if (!partialForm.IsPartialForm()) return merged;
        if (!partialForm.TryGetPresentFields(out var held))
        {
            throw new InvalidOperationException($"Which fields the partial form {partialForm.FormKey} holds isn't known: read it from its plugin (overlay or full import)");
        }
        if (!baseVersion.TryGetPresentFields(out var baseHeld))
        {
            throw new InvalidOperationException($"Which fields the base version of {baseVersion.FormKey} holds isn't known: read it from its plugin (overlay or full import), or merge it first when it is a partial form");
        }
        var heldSet = held.ToHashSet();
        var take = baseHeld.Where(i => !heldSet.Contains(i)).ToHashSet();
        if (take.Count > 0)
        {
            var mask = new (bool On, TranslationCrystal? SubCrystal)[partialForm.Registration.FieldCount];
            foreach (var index in take) mask[index] = (true, null);
            ((IMajorRecordInternal)merged).DeepCopyIn(baseVersion, errorMask: null, copyMask: new TranslationCrystal(mask));
        }
        merged.MajorRecordFlagsRaw &= ~PartialFormFlag;
        PresentFields.Remember(merged, heldSet.Union(baseHeld));
        return merged;
    }
}
