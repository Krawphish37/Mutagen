using Loqui.Generation;
using Noggog.StructuredStrings;
using Noggog.StructuredStrings.CSharp;

namespace Mutagen.Bethesda.Generation.Fields;

/// <summary>
/// C221: a 16-bit half-precision float (System.Half). Equality and hashing go by the bits (HalfBinaryTranslation), as
/// Half's own == treats NaN as unequal to itself and -0 as equal to +0.
/// </summary>
public class HalfType : PrimitiveType
{
    public override Type Type(bool getter) => typeof(Half);

    public override string GenerateEqualsSnippet(Accessor accessor, Accessor rhsAccessor, bool negate = false)
    {
        return $"{(negate ? "!" : null)}HalfBinaryTranslation.BitEquals({accessor.Access}, {rhsAccessor.Access})";
    }

    public override void GenerateForEquals(StructuredStringBuilder sb, Accessor accessor, Accessor rhsAccessor, Accessor maskAccessor)
    {
        if (!this.IntegrateField) return;
        sb.AppendLine($"if ({this.GetTranslationIfAccessor(maskAccessor)})");
        using (sb.CurlyBrace())
        {
            sb.AppendLine($"if ({GenerateEqualsSnippet(accessor, rhsAccessor, negate: true)}) return false;");
        }
    }

    public override void GenerateForEqualsMask(StructuredStringBuilder sb, Accessor accessor, Accessor rhsAccessor, string retAccessor)
    {
        if (!this.IntegrateField) return;
        sb.AppendLine($"{retAccessor} = {GenerateEqualsSnippet(accessor, rhsAccessor)};");
    }

    public override void GenerateForHash(StructuredStringBuilder sb, Accessor accessor, string hashResultAccessor)
    {
        if (!this.IntegrateField) return;
        sb.AppendLine($"{hashResultAccessor}.Add(HalfBinaryTranslation.BitHash({accessor}));");
    }
}
