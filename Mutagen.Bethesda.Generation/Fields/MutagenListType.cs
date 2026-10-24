using Loqui.Generation;
using Noggog.StructuredStrings;
using Noggog.StructuredStrings.CSharp;

namespace Mutagen.Bethesda.Generation.Fields;

public class MutagenListType : ListType
{
    public override void GenerateForEquals(StructuredStringBuilder sb, Accessor accessor, Accessor rhsAccessor, Accessor maskAccessor)
    {
        if (SubTypeGeneration is not ByteArrayType)
        {
            base.GenerateForEquals(sb, accessor, rhsAccessor, maskAccessor);
            return;
        }

        // Byte array items are ReadOnlyMemorySlice structs, which have no content equality of their own,
        // so the default comparer would compare them by reference.  Compare their contents, as GetEqualsMask does.
        sb.AppendLine($"if ({GetTranslationIfAccessor(maskAccessor)})");
        using (sb.CurlyBrace())
        {
            sb.AppendLine($"if (!{accessor.Access}.{(Nullable ? "SequenceEqualNullable" : "SequenceEqual")}({rhsAccessor.Access}, (l, r) => {SubTypeGeneration.GenerateEqualsSnippet(new Accessor("l"), new Accessor("r"))})) return false;");
        }
    }
}
