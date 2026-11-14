using Mutagen.Bethesda.Plugins;

namespace Mutagen.Bethesda.Starfield;

public partial class MagicEffectTrackDamageArchetype
{
    public MagicEffectArchetype.TypeEnum Type => MagicEffectArchetype.TypeEnum.TrackDamage;
    public override IFormLinkIdentifier AssociationKey => Association;
}
