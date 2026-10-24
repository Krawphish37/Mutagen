using Loqui.Generation;
using Noggog.StructuredStrings;

namespace Mutagen.Bethesda.Generation.Fields;

public class MutagenArray2dType : Array2dType
{
    public override void GenerateForHash(StructuredStringBuilder sb, Accessor accessor, string hashResultAccessor)
    {
        // Enumerates as point/value pairs
        MutagenListType.GenerateForContainerHash(this, sb, accessor, hashResultAccessor, subItemSuffix: ".Value");
    }
}
