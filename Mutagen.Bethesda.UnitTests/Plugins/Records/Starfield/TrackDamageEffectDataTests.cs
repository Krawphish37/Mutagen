using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;
using TempFile = Noggog.IO.TempFile;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C195: TDED (Track Damage Effect Data), xEdit's EDID + REFL record, linked by MGEF archetype 54 (Track Damage). Mutagen
// had no TDED record type, so archetype 54 read as the generic archetype. No official plugin holds a TDED; its one use
// (ModLegendaryEffect_AbyssalRelease_TrackDMG 02632B:SFBGS00D.esm) has no association. So these use synthetic records.
public class TrackDamageEffectDataTests
{
    // AKDecalWallPaintWhite (001DA3:Starfield.esm) MTPT REFL, borrowed as a valid reflection blob without FormIDs
    private const string Refl = "42455448080000000400000005000000535452541D0000004247534D6174657269616C50617468466F726D004D6174657269616C00545950450400000001000000434C4153180000000000000000000000000001001400000002FFFFFF180108004F424A54390000000000000033004172636869746563747572655C436974795C416B696C615C446563616C5C414B57616C6C5061696E7457686974652E6D617400";

    private static (StarfieldMod Full, IStarfieldModDisposableGetter Overlay, byte[] Written, TempFile File) WriteAndRead(StarfieldMod mod)
    {
        var tmp = new TempFile(extraDirectoryPaths: TestPathing.TempFolderPath, suffix: ".esp");
        var path = new ModPath(mod.ModKey, tmp.File.Path);
        mod.BeginWrite.ToPath(path).WithNoLoadOrder().NoModKeySync().Write();
        var full = StarfieldMod.CreateFromBinary(path, StarfieldRelease.Starfield);
        var overlay = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield);
        return (full, overlay, File.ReadAllBytes(tmp.File.Path), tmp);
    }

    [Fact]
    public void TrackDamageEffectData_RoundTrips_BothReaders()
    {
        var mod = new StarfieldMod(ModKey.FromNameAndExtension("TrackDamage.esp"), StarfieldRelease.Starfield);
        var data = new TrackDamageEffectData(mod) { EditorID = "TestTrackDamage", Reflection = new Reflection { Data = Hex(Refl) } };
        mod.TrackDamageEffectDatas.Add(data);
        var empty = new TrackDamageEffectData(mod) { EditorID = "TestTrackDamageEmpty" };
        mod.TrackDamageEffectDatas.Add(empty);

        var (full, overlay, written, tmp) = WriteAndRead(mod);
        using (tmp)
        using (overlay)
        {
            foreach (var group in new IEnumerable<ITrackDamageEffectDataGetter>[] { full.TrackDamageEffectDatas.Records, overlay.TrackDamageEffectDatas.Records })
            {
                var records = group.ToArray();
                records.Select(r => r.EditorID).ShouldBe(new[] { "TestTrackDamage", "TestTrackDamageEmpty" });
                records[0].Reflection!.Data.ToArray().ShouldBe(Hex(Refl));
                records[1].Reflection.ShouldBeNull();
                records[0].Equals(data).ShouldBeTrue();
            }

            // the group is TDED and the records write back as read, from both readers
            using var again = new TempFile(extraDirectoryPaths: TestPathing.TempFolderPath, suffix: ".esp");
            full.BeginWrite.ToPath(new ModPath(mod.ModKey, again.File.Path)).WithNoLoadOrder().NoModKeySync().Write();
            File.ReadAllBytes(again.File.Path).ShouldBe(written);
            using var again2 = new TempFile(extraDirectoryPaths: TestPathing.TempFolderPath, suffix: ".esp");
            overlay.WriteToBinary(again2.File.Path, new Mutagen.Bethesda.Plugins.Binary.Parameters.BinaryWriteParameters
            {
                ModKey = Mutagen.Bethesda.Plugins.Binary.Parameters.ModKeyOption.NoCheck,
                MastersListContent = Mutagen.Bethesda.Plugins.Binary.Parameters.MastersListContentOption.NoCheck,
            });
            File.ReadAllBytes(again2.File.Path).ShouldBe(written);
            System.Text.Encoding.ASCII.GetString(written).ShouldContain("TDED");
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TrackDamageArchetype_LinksTheEffectData(bool typed)
    {
        var mod = new StarfieldMod(ModKey.FromNameAndExtension("TrackDamageArchetype.esp"), StarfieldRelease.Starfield);
        var data = new TrackDamageEffectData(mod) { EditorID = "TestTrackDamage" };
        mod.TrackDamageEffectDatas.Add(data);
        AMagicEffectArchetype archetype = typed
            ? new MagicEffectTrackDamageArchetype { Association = data.ToLink<ITrackDamageEffectDataGetter>() }
            : new MagicEffectArchetype(MagicEffectArchetype.TypeEnum.TrackDamage) { Association = new FormLink<IStarfieldMajorRecordGetter>(data.FormKey) };
        mod.MagicEffects.Add(new MagicEffect(mod) { EditorID = "TestEffect", Archetype = archetype });

        var (full, overlay, written, tmp) = WriteAndRead(mod);
        using (tmp)
        using (overlay)
        {
            foreach (var read in new IMagicEffectGetter[] { full.MagicEffects.Records.Single(), overlay.MagicEffects.Records.Single() })
            {
                read.Archetype.Type.ShouldBe(MagicEffectArchetype.TypeEnum.TrackDamage);
                read.Archetype.ShouldBeOfType<MagicEffectTrackDamageArchetype>().Association.FormKey.ShouldBe(data.FormKey);
                read.EnumerateFormLinks().ShouldContain(l => l.FormKey == data.FormKey && l.Type == typeof(ITrackDamageEffectDataGetter));
            }
            full.ToImmutableLinkCache().Resolve<ITrackDamageEffectDataGetter>(data.FormKey).EditorID.ShouldBe("TestTrackDamage");

            using var again = new TempFile(extraDirectoryPaths: TestPathing.TempFolderPath, suffix: ".esp");
            full.BeginWrite.ToPath(new ModPath(mod.ModKey, again.File.Path)).WithNoLoadOrder().NoModKeySync().Write();
            File.ReadAllBytes(again.File.Path).ShouldBe(written);
        }
    }
}
