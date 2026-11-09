using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Field names from xEdit for fields Mutagen read but called Unknown or by their signature: the contact shadow
// component (FLCS), the animation graph component (ANAM-DNAM) and the last PTT2 transform. Bytes from Starfield.esm.
public class ComponentFieldNameTests
{
    private static byte[] Z(string s) => Encoding.ASCII.GetBytes(s + "\0");

    [Fact]
    public void ContactShadow()
    {
        // ShpIndIntAddOn_FramePanel01 (STAT 090A9A): 0.009, 3, 10, 0.2, 0.06
        const string flcs = "BC-74-13-3C-00-00-40-40-00-00-20-41-CD-CC-4C-3E-8F-C2-75-3D";
        var bytes = Record("STAT", Subrecord("BFCB", Z("BGSContactShadowComponent_Component")), Subrecord("FLCS", flcs), Subrecord("BFCE", ""));
        foreach (var stat in ReadBoth<IStaticGetter>(bytes, f => Static.CreateFromBinary(f), (s, p) => StaticBinaryOverlay.StaticFactory(s, p)))
        {
            var item = stat.Components.OfType<IContactShadowComponentGetter>().Single().Items.Single();
            item.Thickness.ShouldBe(BitConverter.ToSingle(Hex("BC-74-13-3C")));
            item.FadeLength.ShouldBe(3f);
            item.FadeStart.ShouldBe(10f);
            item.MaxDistance.ShouldBe(0.2f);
            item.ProximityLimit.ShouldBe(0.06f);
            var written = GameConstants.Starfield.MajorRecord(Write(w => stat.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("FLCS")).Content.ToArray().ShouldBe(Hex(flcs));
        }
    }

    [Fact]
    public void AnimationGraph()
    {
        // Razorback (WEAP 000FD6)
        var bytes = Record("WEAP", Subrecord("BFCB", Z("BGSAnimationGraph_Component")),
            Subrecord("ANAM", Z(@"AnimTextData\Tables\Graphs\FXAmmoCounterIndex.agx")),
            Subrecord("BNAM", Z(@"genericbehaviors\characterassets\skeleton.rig")),
            Subrecord("BFCE", ""));
        foreach (var weap in ReadBoth<IWeaponGetter>(bytes, f => Weapon.CreateFromBinary(f), (s, p) => WeaponBinaryOverlay.WeaponFactory(s, p)))
        {
            var comp = weap.Components.OfType<IAnimationGraphComponentGetter>().Single();
            comp.RootAnimationGraph.ShouldBe(@"AnimTextData\Tables\Graphs\FXAmmoCounterIndex.agx");
            comp.RigFile.ShouldBe(@"genericbehaviors\characterassets\skeleton.rig");
            comp.AnimationsPath.ShouldBeNull();
            comp.ResponseFile.ShouldBeNull();
        }
    }

    [Fact]
    public void Transforms_ResearchMenu()
    {
        // Chem_XenoAurora (ALCH 0C8721): its research menu transform is
        // Inv_DefaultTransform_Z180_Aurora_NOCLUTTER_ResearchUI2 (04AC26)
        const string ptt2 = "3A-DE-0B-00-00-00-00-00-00-00-00-00-00-00-00-00-18-45-05-00-24-AC-04-00-00-00-00-00-26-AC-04-00";
        foreach (var alch in ReadBoth<IIngestibleGetter>(Record("ALCH", Subrecord("PTT2", ptt2)), f => Ingestible.CreateFromBinary(f), (s, p) => IngestibleBinaryOverlay.IngestibleFactory(s, p)))
        {
            alch.Transforms!.ResearchMenu.FormKey.ID.ShouldBe(0x04AC26u);
            var written = GameConstants.Starfield.MajorRecord(Write(w => alch.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("PTT2")).Content.ToArray().ShouldBe(Hex(ptt2));
        }
    }
}
