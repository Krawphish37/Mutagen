namespace Mutagen.Bethesda.Starfield;

public partial class Resource
{
    public enum RarityEnum
    {
        Common,
        Uncommon,
        Rare,
        Exotic,
        Unique,
        // xEdit: Special and Everywhere. In Starfield.esm 5 is only on Helium-3; 6 is on Water and on four
        // manufactured primers (Small Primer, Large Primer, Charged Diodes, Magnetic Diodes), so it is not "H2O only"
        Special,
        Everywhere,
    }
}