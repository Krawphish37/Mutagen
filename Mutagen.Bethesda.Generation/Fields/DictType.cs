using System.Xml.Linq;
using Loqui.Generation;
using Noggog;
using Noggog.StructuredStrings;
using Noggog.StructuredStrings.CSharp;

namespace Mutagen.Bethesda.Generation.Fields;

public class DictType : Loqui.Generation.DictType
{
    /// <summary>
    /// This parameter is necessessary if the enums are not accessible at generation time by the generation program.
    /// If they are, this parameter can be removed in favor of reflection
    /// </summary>
    public byte? NumEnumKeys;

    public override async Task Load(XElement node, bool requireName = true)
    {
        await base.Load(node, requireName);
        if (node.TryGetAttribute("numEnumKeys", out byte num))
        {
            NumEnumKeys = num;
        }
    }

    public override string GenerateEqualsSnippet(Accessor accessor, Accessor rhsAccessor, bool negate = false)
    {
        // Keyed value dictionaries (record caches) keep the record order they are written in, so leave them order sensitive
        if (Mode != DictMode.KeyValue) return base.GenerateEqualsSnippet(accessor, rhsAccessor, negate);
        // Compare entries by key, so the same content added in a different order is still equal, as GetEqualsMask does
        return $"{(negate ? "!" : null)}{accessor.Access}.{nameof(ReadOnlyDictionaryExt.DictEqualsNullable)}({rhsAccessor.Access})";
    }

    public override void GenerateForEquals(StructuredStringBuilder sb, Accessor accessor, Accessor rhsAccessor, Accessor maskAccessor)
    {
        if (Mode != DictMode.KeyValue)
        {
            base.GenerateForEquals(sb, accessor, rhsAccessor, maskAccessor);
            return;
        }
        sb.AppendLine($"if ({GetTranslationIfAccessor(maskAccessor)})");
        using (sb.CurlyBrace())
        {
            sb.AppendLine($"if ({GenerateEqualsSnippet(accessor, rhsAccessor, negate: true)}) return false;");
        }
    }

    public override void GenerateForHash(StructuredStringBuilder sb, Accessor accessor, string hashResultAccessor)
    {
        // Equals compares the entries, so hash the entries rather than the dictionary object itself
        if (this.Nullable)
        {
            sb.AppendLine($"if ({accessor} is {{}} {this.Name}item)");
            accessor = $"{this.Name}item";
        }
        using (sb.CurlyBrace(doIt: this.Nullable))
        {
            sb.AppendLine($"foreach (var subItem in {accessor})");
            using (sb.CurlyBrace())
            {
                sb.AppendLine($"{hashResultAccessor}.Add(subItem.Key);");
                sb.AppendLine($"{hashResultAccessor}.Add(subItem.Value);");
            }
        }
    }
}
