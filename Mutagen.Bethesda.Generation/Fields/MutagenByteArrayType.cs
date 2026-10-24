using Loqui.Generation;
using Noggog.StructuredStrings;
using Noggog.StructuredStrings.CSharp;

namespace Mutagen.Bethesda.Generation.Fields;

public class MutagenByteArrayType : ByteArrayType
{
    public override void GenerateForHash(StructuredStringBuilder sb, Accessor accessor, string hashResultAccessor)
    {
        if (!this.IntegrateField) return;
        // Equals compares the bytes, while the memory slice struct itself hashes its backing array by reference
        if (this.Nullable)
        {
            sb.AppendLine($"if ({accessor} is {{}} {this.Name}Item)");
            accessor = $"{this.Name}Item";
        }
        using (sb.CurlyBrace(doIt: this.Nullable))
        {
            sb.AppendLine($"{hashResultAccessor}.AddBytes({accessor});");
        }
    }
}
