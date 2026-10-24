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

    public override void GenerateForHash(StructuredStringBuilder sb, Accessor accessor, string hashResultAccessor)
    {
        GenerateForContainerHash(this, sb, accessor, hashResultAccessor);
    }

    /// <summary>
    /// Equals compares containers by their contents, so the hash has to be built from the contents as well.
    /// Adding the container object itself to the hash would hash it by reference.
    /// </summary>
    public static void GenerateForContainerHash(
        WrapperType field,
        StructuredStringBuilder sb,
        Accessor accessor,
        string hashResultAccessor,
        string subItemSuffix = "")
    {
        if (field.Nullable)
        {
            sb.AppendLine($"if ({accessor} is {{}} {field.Name}item)");
            accessor = $"{field.Name}item";
        }
        using (sb.CurlyBrace(doIt: field.Nullable))
        {
            sb.AppendLine($"foreach (var subItem in {accessor})");
            using (sb.CurlyBrace())
            {
                if (field.SubTypeGeneration is ByteArrayType && !field.SubTypeGeneration.IsNullable)
                {
                    sb.AppendLine($"{hashResultAccessor}.AddBytes(subItem{subItemSuffix});");
                }
                else
                {
                    sb.AppendLine($"{hashResultAccessor}.Add(subItem{subItemSuffix});");
                }
            }
        }
    }
}
