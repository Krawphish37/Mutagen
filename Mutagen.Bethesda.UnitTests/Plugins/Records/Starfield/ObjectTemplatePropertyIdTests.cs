using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Object templates (OBTS) on WEAP, ARMO and NPC_ store small property ids (xEdit wbObjectModProperties{WEAP,ARMO,NPC}Enum),
// while OMOD records store 4-letter codes. Both read into the same Weapon/Armor/Npc.Property enums, so the template ids
// are members of those enums too. In Starfield.esm: ARMO 14 (297), NPC_ 6 (10), WEAP 28, 29, 71 (4 each) and 85 (2).
public class ObjectTemplatePropertyIdTests
{
    // OBTS 13 of 14 on InflictorRifle (WEAP 26D8A0): 7 includes, then properties 28 (Float), 29 (Float), 71 (FormID,Float)
    private const string InflictorRifleObts =
        "07-00-00-00-03-00-00-00-01-00-1D-00-00-00-00-01-B9-9A-32-00-00-00-C4-C0-31-00-00-00-01-BA-A0-0E-00-00-00-01-42-F4-0F-00-00-00-01-C1-30-12-00-00-00-01-BD-30-12-00-00-00-01-B4-30-12-00-00-00-01-B8-30-12-00-00-00-01-01-00-00-00-01-00-00-00-1C-00-00-00-CD-CC-0C-BF-00-00-00-00-00-00-00-00-01-00-00-00-01-00-00-00-1D-00-00-00-00-00-A0-BF-00-00-00-00-00-00-00-00-06-00-00-00-01-00-00-00-47-00-00-00-81-0A-06-00-CD-CC-0C-BF-00-00-00-00";
    // OBTS of ARMO 0028A5: 3 includes, then property 14 (FormID,Int), a layered material swap
    private const string ArmorObts =
        "03-00-00-00-01-00-00-00-00-00-00-00-FF-FF-01-00-00-00-B2-5A-1C-00-00-00-01-4A-AF-25-00-00-00-01-FD-DD-13-00-00-00-01-04-00-00-00-00-00-00-00-0E-00-00-00-83-28-00-00-00-00-00-00-00-00-00-00";
    // OBTS of NPC_ 112374: property 6 (FormID,Int), a layered material swap
    private const string NpcObts =
        "00-00-00-00-01-00-00-00-00-00-00-00-FF-FF-01-00-00-00-04-00-00-00-00-00-00-00-06-00-00-00-2F-8C-3E-00-00-00-00-00-00-00-00-00";

    private static byte[] Templated(string sig, string obts) =>
        Record(sig, Subrecord("OBTE", "01-00-00-00"), Subrecord("OBTS", obts), Subrecord("STOP", Array.Empty<byte>()));

    [Fact]
    public void Weapon_TemplateIds_Read()
    {
        foreach (var weapon in ReadBoth<IWeaponGetter>(Templated("WEAP", InflictorRifleObts), f => Weapon.CreateFromBinary(f), (s, p) => WeaponBinaryOverlay.WeaponFactory(s, p)))
        {
            var props = weapon.ObjectTemplates!.Single().Properties.Select(p => p.Property).ToArray();
            props.ShouldBe(new[] { Weapon.Property.TemplatePhysicalWeaponDamage, Weapon.Property.TemplateUnknown29, Weapon.Property.TemplateDamageTypeValue });
        }
    }

    [Fact]
    public void Armor_TemplateId_Reads()
    {
        foreach (var armor in ReadBoth<IArmorGetter>(Templated("ARMO", ArmorObts), f => Armor.CreateFromBinary(f), (s, p) => ArmorBinaryOverlay.ArmorFactory(s, p)))
            armor.ObjectTemplates!.Single().Properties.Single().Property.ShouldBe(Armor.Property.TemplateLayeredMaterialSwap);
    }

    [Fact]
    public void Npc_TemplateId_Reads()
    {
        foreach (var npc in ReadBoth<INpcGetter>(Templated("NPC_", NpcObts), f => Npc.CreateFromBinary(f), (s, p) => NpcBinaryOverlay.NpcFactory(s, p)))
            npc.ObjectTemplates!.Single().Properties.Single().Property.ShouldBe(Npc.Property.TemplateLayeredMaterialSwap);
    }

    [Fact]
    public void Weapon_Template_WritesSameBytes()
    {
        foreach (var weapon in ReadBoth<IWeaponGetter>(Templated("WEAP", InflictorRifleObts), f => Weapon.CreateFromBinary(f), (s, p) => WeaponBinaryOverlay.WeaponFactory(s, p)))
        {
            var written = GameConstants.Starfield.MajorRecord(Write(w => weapon.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("OBTS")).Content.ToArray().ShouldBe(Hex(InflictorRifleObts));
        }
    }

    [Fact]
    public void OmodCodes_Unchanged()
    {
        ((uint)Weapon.Property.DamagePhysical).ShouldBe((uint)new RecordType("WDMG").TypeInt);
        ((uint)Armor.Property.LayeredMaterialSwap).ShouldBe((uint)new RecordType("AMLS").TypeInt);
        ((uint)Npc.Property.LayeredMaterialSwap).ShouldBe((uint)new RecordType("NMSL").TypeInt);
    }
}
