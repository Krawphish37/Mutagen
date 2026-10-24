using Loqui.Generation;
using Noggog.StructuredStrings;
using Noggog.StructuredStrings.CSharp;

namespace Mutagen.Bethesda.Generation.Fields;

public class MutagenColorType : ColorType
{
    public override void GenerateForHash(StructuredStringBuilder sb, Accessor accessor, string hashResultAccessor)
    {
        if (!this.IntegrateField) return;
        // Equals uses ColorOnlyEquals (ARGB), while Color.GetHashCode also considers the name/known color state
        var nullable = this.Nullable && this.CanBeNullable(getter: true);
        if (nullable)
        {
            sb.AppendLine($"if ({accessor} is {{}} {this.Name}item)");
            accessor = $"{this.Name}item";
        }
        using (sb.CurlyBrace(doIt: nullable))
        {
            sb.AppendLine($"{hashResultAccessor}.Add({accessor}.ToArgb());");
        }
    }
}
