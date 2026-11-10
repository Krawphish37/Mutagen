using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// XALG was an unknown UInt64 on 34 record types. It is one flags enum, XalgFlag, with xEdit's names (its list is
// Fallout 76's); in Starfield.esm the checkable names fit: Disable Physics Sim on books and keys, References Are
// Moveable on projectiles, Disable Audio Occlusion/Obstruction on sound markers. Real XALG values, both readers,
// written back byte for byte; bits nobody names still round-trip.
public class XalgFlagTests
{
    private static void Check<T>(string sig, string xalg, XalgFlag expected,
        Func<MutagenFrame, T> full, Func<OverlayStream, BinaryOverlayFactoryPackage, T> overlay, Func<T, XalgFlag?> get)
        where T : IMajorRecordGetter
    {
        var bytes = Record(sig, Subrecord("XALG", xalg));
        foreach (var rec in ReadBoth(bytes, full, overlay))
        {
            get(rec).ShouldBe(expected);
            var written = GameConstants.Starfield.MajorRecord(Write(w => ((IBinaryItem)rec).WriteToBinary(w)));
            written.FindSubrecord(new RecordType("XALG")).Content.ToArray().ShouldBe(Hex(xalg));
        }
    }

    // CF_KryxHistoryInterview_Slate001 (001CF1:Starfield.esm)
    [Fact]
    public void Book_DisablePhysicsSim()
        => Check<IBookGetter>("BOOK", "0800000000000000", XalgFlag.DisablePhysicsSim,
            f => Book.CreateFromBinary(f), (s, p) => BookBinaryOverlay.BookFactory(s, p), x => x.XalgFlags);

    // AMB_CF07_LegacyNormal_SilentSwitchMarker (003DA8:Starfield.esm)
    [Fact]
    public void SoundMarker_DisableAudioOcclusion()
        => Check<ISoundMarkerGetter>("SOUN", "2000000000000000", XalgFlag.DisableAudioOcclusionObstruction,
            f => SoundMarker.CreateFromBinary(f), (s, p) => SoundMarkerBinaryOverlay.SoundMarkerFactory(s, p), x => x.XalgFlags);

    // GenericBallisticProjectile_Penetrator (025FC0:Starfield.esm)
    [Fact]
    public void Projectile_MoveableAndIgnoreAudioHits()
        => Check<IProjectileGetter>("PROJ", "4004000000000000", XalgFlag.ReferencesAreMoveable | XalgFlag.IgnoreAudioObstructionHits,
            f => Projectile.CreateFromBinary(f), (s, p) => ProjectileBinaryOverlay.ProjectileFactory(s, p), x => x.XalgFlags);

    // REFR 0BDA69:Starfield.esm
    [Fact]
    public void Placed_NonInteractiveNoPhysics()
        => Check<IPlacedObjectGetter>("REFR", "0802000000000000", XalgFlag.NonInteractive | XalgFlag.DisablePhysicsSim,
            f => PlacedObject.CreateFromBinary(f), (s, p) => PlacedObjectBinaryOverlay.PlacedObjectFactory(s, p), x => x.XalgFlags);

    // A bit no list names (63) is kept and written back
    [Fact]
    public void UnnamedBit_RoundTrips()
        => Check<IStaticGetter>("STAT", "0400000000000080", XalgFlag.Unknown2 | (XalgFlag)0x8000_0000_0000_0000,
            f => Static.CreateFromBinary(f), (s, p) => StaticBinaryOverlay.StaticFactory(s, p), x => x.XalgFlags);
}
