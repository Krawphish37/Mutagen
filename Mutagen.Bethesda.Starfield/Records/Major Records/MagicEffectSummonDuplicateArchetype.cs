using Mutagen.Bethesda.Plugins;

namespace Mutagen.Bethesda.Starfield;

public partial class MagicEffectSummonDuplicateArchetype
{
    public MagicEffectArchetype.TypeEnum Type => MagicEffectArchetype.TypeEnum.SummonDuplicate;
    public override IFormLinkIdentifier AssociationKey => Association;
}
