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

    public override void GenerateForCopy(StructuredStringBuilder sb, Accessor accessor, Accessor rhs, Accessor copyMaskAccessor, bool protectedMembers, bool deepCopy)
    {
        // Nullable fixed size arrays of links and other items that need duplicating
        if (!this.FixedSize.HasValue
            || !this.Nullable
            || this.SubTypeGeneration is LoquiType
            || this.SubTypeGeneration.GetDuplicate("x") is not {} duplicate)
        {
            base.GenerateForCopy(sb, accessor, rhs, copyMaskAccessor, protectedMembers, deepCopy);
            return;
        }

        // Duplicate each item, as the non-nullable path does,
        // rather than copying the item references over
        if (!this.AlwaysCopy)
        {
            sb.AppendLine($"if ({(deepCopy ? this.GetTranslationIfAccessor(copyMaskAccessor) : this.SkipCheck(copyMaskAccessor, deepCopy))})");
        }
        using (sb.CurlyBrace(doIt: !this.AlwaysCopy))
        {
            sb.AppendLine($"{accessor} = {rhs}?.Select(x => {duplicate}).ToArray<{this.ItemTypeName(getter: false)}>();");
        }
    }

    public override void GenerateForEquals(StructuredStringBuilder sb, Accessor accessor, Accessor rhsAccessor, Accessor maskAccessor)
    {
        if (!this.Nullable)
        {
            base.GenerateForEquals(sb, accessor, rhsAccessor, maskAccessor);
            return;
        }

        // Nullable arrays of links and other non-primitive items.
        // Checked first: link items are not IEquatable of their own type, so the MemorySliceExt helper does not apply to them
        if (this.SubTypeGeneration is not PrimitiveType)
        {
            // Only compare the spans when both sides have a value,
            // rather than a NullSame check followed by an unconditional .Value access
            sb.AppendLine($"if ({this.GetTranslationIfAccessor(maskAccessor)})");
            using (sb.CurlyBrace())
            {
                sb.AppendLine($"if (!{nameof(ObjectExt)}.{nameof(ObjectExt.NullSame)}({accessor.Access}, {rhsAccessor.Access})) return false;");
                sb.AppendLine($"if ({accessor.Access} is {{}} {this.Name}Lhs");
                using (sb.IncreaseDepth())
                {
                    sb.AppendLine($"&& {rhsAccessor.Access} is {{}} {this.Name}Rhs");
                    sb.AppendLine($"&& !MemoryExtensions.SequenceEqual<{this.SubTypeGeneration.TypeName(getter: true)}>({this.Name}Lhs.Span!, {this.Name}Rhs.Span!)) return false;");
                }
            }
            return;
        }

        if (!this.SimpleTarget
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
