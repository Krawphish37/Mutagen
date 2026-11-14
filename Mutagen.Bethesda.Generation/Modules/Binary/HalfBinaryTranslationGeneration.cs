using Loqui.Generation;

namespace Mutagen.Bethesda.Generation.Modules.Binary;

// C221: half-precision floats, read and written by their 16 bits
public class HalfBinaryTranslationGeneration : PrimitiveBinaryTranslationGeneration<Half>
{
    public HalfBinaryTranslationGeneration()
        : base(expectedLen: 2)
    {
        PreferDirectTranslation = false;
    }

    public override bool NeedsGenerics => false;

    public override string GenerateForTypicalWrapper(
        ObjectGeneration objGen,
        TypeGeneration typeGen,
        Accessor dataAccessor,
        Accessor packageAccessor)
    {
        return $"HalfBinaryTranslation.Read({dataAccessor})";
    }
}
