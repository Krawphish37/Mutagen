using Loqui.Generation;
using Noggog;
using Noggog.StructuredStrings;
using Noggog.StructuredStrings.CSharp;

namespace Mutagen.Bethesda.Generation.Fields;

public class MutagenArrayType : ArrayType
{
    public override void GenerateForHash(StructuredStringBuilder sb, Accessor accessor, string hashResultAccessor)
    {
        MutagenListType.GenerateForContainerHash(this, sb, accessor, hashResultAccessor);
    }

    public override void GenerateForEquals(StructuredStringBuilder sb, Accessor accessor, Accessor rhsAccessor, Accessor maskAccessor)
    {
        if (!this.SimpleTarget
            || !this.Nullable
            || !this.SubTypeGeneration.IsIEquatable)
        {
            base.GenerateForEquals(sb, accessor, rhsAccessor, maskAccessor);
            return;
        }

        // Nullable spans are compared via a helper that handles both sides being null,
        // rather than a NullSame check followed by an unconditional .Value access
        sb.AppendLine($"if ({this.GetTranslationIfAccessor(maskAccessor)})");
        using (sb.CurlyBrace())
        {
            sb.AppendLine($"if (!{nameof(MemorySliceExt)}.{nameof(MemorySliceExt.SequenceEqual)}({accessor.Access}, {rhsAccessor.Access})) return false;");
        }
    }
}
