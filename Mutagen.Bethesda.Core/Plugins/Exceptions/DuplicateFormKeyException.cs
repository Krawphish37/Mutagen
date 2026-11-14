using System.Text;
using Mutagen.Bethesda.Plugins.Records;

namespace Mutagen.Bethesda.Plugins.Exceptions;

/// <summary>
/// Two records of one plugin whose stored FormIDs read as the same FormKey
/// </summary>
/// <param name="FormKey">The FormKey both read as</param>
/// <param name="FirstType">The earlier record's type</param>
/// <param name="FirstEditorID">Its EditorID, if any</param>
/// <param name="FirstFormID">Its stored FormID</param>
/// <param name="SecondType">The later record's type</param>
/// <param name="SecondEditorID">Its EditorID, if any</param>
/// <param name="SecondFormID">Its stored FormID</param>
public sealed record DuplicateFormKeyPair(
    FormKey FormKey,
    RecordType FirstType,
    string? FirstEditorID,
    uint FirstFormID,
    RecordType SecondType,
    string? SecondEditorID,
    uint SecondFormID)
{
    public override string ToString() =>
        $"{FormKey}: {FirstType} {FirstFormID:X8} {FirstEditorID ?? "(no EditorID)"} and {SecondType} {SecondFormID:X8} {SecondEditorID ?? "(no EditorID)"}";
}

/// <summary>
/// A plugin whose records share a FormKey, refused by default (BinaryReadParameters.DuplicateFormKeys). The game keeps
/// one of each pair; Mutagen can't tell which one a link means. Read it with DuplicateFormKeyOption.KeepLater to look
/// inside. A RecordException, so the readers pass it on as it is (not wrapped).
/// </summary>
public sealed class DuplicateFormKeyException : RecordException
{
    /// <summary>Every pair of records that share a FormKey, in file order</summary>
    public IReadOnlyList<DuplicateFormKeyPair> Pairs { get; }

    public DuplicateFormKeyException(ModKey modKey, IReadOnlyList<DuplicateFormKeyPair> pairs)
        : base(pairs[0].FormKey, null, modKey, pairs[0].SecondEditorID, BuildMessage(modKey, pairs))
    {
        Pairs = pairs;
    }

    private static string BuildMessage(ModKey modKey, IReadOnlyList<DuplicateFormKeyPair> pairs)
    {
        var sb = new StringBuilder();
        sb.Append($"{modKey} refused: {pairs.Count} pair{(pairs.Count == 1 ? "" : "s")} of its records share a FormKey (the plugin was probably written for another master list); ");
        sb.Append(string.Join("; ", pairs));
        sb.Append(". Read it with BinaryReadParameters.DuplicateFormKeys = KeepLater to look inside.");
        return sb.ToString();
    }
}
