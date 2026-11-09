namespace Mutagen.Bethesda.Starfield;

public partial class PlanetContentManagerBranchNode
{
    public enum NodeTypeOption
    {
        Root = 0x00, // only on trees (PCMT)
        BranchNode = 0x01,
        ContentNode = 0x02,
    }
    
    public enum ChildSelectionOption
    {
        Random,
        Stacked,
    }
}