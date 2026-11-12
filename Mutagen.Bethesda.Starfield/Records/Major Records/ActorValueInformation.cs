namespace Mutagen.Bethesda.Starfield;

partial class ActorValueInformation
{
    [Flags]
    public enum Flag : uint
    {
        Skill = 0x02,
        UsesEnum = 0x04,
        DoNotAllowScriptEdits = 0x8,
        IsFullAvCached = 0x10,
        IsPermanentAvCached = 0x020,
        Unknown6 = 0x0000_0040,
        DefaultToZero = 0x400,
        DefaultToOne = 0x800,
        DefaultToOneHundred = 0x1000,
        Unknown13 = 0x0000_2000,
        ContainsList = 0x8000,
        Unknown16 = 0x0001_0000,
        ValueLessThanOne = 0x8_0000,
        MinimumOne = 0x10_0000,
        MaximumTen = 0x20_0000,
        MaximumOneHundred = 0x40_0000,
        MultiplyByOneHundred = 0x80_0000,
        Percentage = 0x100_0000,
        Unknown25 = 0x0200_0000,
        DamageIsPositive = 0x400_0000,
        GodModeImmune = 0x800_0000,
        Unknown28 = 0x1000_0000,
        Hardcoded = 0x8000_0000,
    }

    public enum Types
    {
        DerivedAttribute,
        SpecialAttribute,
        Skill,
        AiAttribute,
        Resistance,
        Condition,
        Charge,
        IntValue,
        Variable,
        Resource,
        AnimGraphVariable,
    }
}