using System.Xml.Linq;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using TempFile = Noggog.IO.TempFile;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C80 (S171): the manifest's offsets are where the reader reads each value.
// - Every game's manifest: an object's offsets in one block run in field order without overlapping.
// - Written records: every offset held to the value positions (ManifestOffsetCheck).
public partial class PropertyManifestTests
{
    // Another game's manifest (beside Starfield's project)
    private static XElement Manifest(string game) =>
        XDocument.Load(Path.Combine(ProjectDir(), "..", $"Mutagen.Bethesda.{game}", "Manifest", "Properties.xml")).Root!;

    // C80: before, every Loqui field of one type in a block had the last one's offset (Loqui's LoquiType equals any other
    // of the same target object, and the offsets were kept in a dictionary comparing by value), and a record's sound
    // subrecords (PickupSound) took a data subrecord member's offset
    [Theory]
    [InlineData("Starfield")]
    [InlineData("Skyrim")]
    [InlineData("Fallout4")]
    [InlineData("Fallout3")]
    [InlineData("Oblivion")]
    public void Offsets_RunInFieldOrderWithoutOverlapping(string game)
    {
        var problems = new List<string>();
        var count = 0;
        foreach (var obj in Manifest(game).Elements("Object"))
        {
            var name = (string)obj.Attribute("name")!;
            var record = (string?)obj.Attribute("objType") == "Record";
            var ends = new Dictionary<string, (int End, string Field)>();
            foreach (var e in obj.Elements().Where(e => e.Attribute("offset") != null && e.Attribute("derived") == null))
            {
                count++;
                var field = $"{name}.{(string?)e.Attribute("name") ?? e.Name.LocalName}";
                var block = (string?)e.Attribute("data") ?? "";
                if (record && block.Length == 0)
                {
                    problems.Add($"{field}: a record's field outside a data subrecord has offset {e.Attribute("offset")!.Value}");
                    continue;
                }
                var offset = (int)e.Attribute("offset")!;
                if (ends.TryGetValue(block, out var before) && offset < before.End)
                {
                    problems.Add($"{field}: offset {offset} in {(block.Length == 0 ? "its object" : block)}, before the end of {before.Field} ({before.End})");
                }
                ends[block] = (offset + ((int?)e.Attribute("length") ?? 0), field);
            }
        }
        count.ShouldBeGreaterThan(400);
        problems.ShouldBeEmpty(string.Join(Environment.NewLine, problems.Take(40)));
    }

    private static List<RecordValuePositions> WrittenPositions(StarfieldMod mod)
    {
        using var tmp = new TempFile(extraDirectoryPaths: TestPathing.TempFolderPath, suffix: ".esp");
        var path = new ModPath(mod.ModKey, tmp.File.Path);
        mod.BeginWrite.ToPath(path).WithNoLoadOrder().NoModKeySync().Write();
        using var overlay = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield);
        return overlay.EnumerateMajorRecords().Select(r => r.GetValuePositions()).ToList();
    }

    // C80: written records holding S171's objects (Weapon WAUD's sounds, MovementData's speeds, CombatStyle's space
    // tables, Explosion's sounds), every manifest offset in them held to where the reader read the value
    [Fact]
    public void LoquiFieldsOfOneType_AreAtTheOffsetsTheReaderReads()
    {
        var mod = new StarfieldMod(ModKey.FromNameAndExtension("ManifestOffsets.esp"), StarfieldRelease.Starfield);
        mod.Weapons.Add(new Weapon(mod));
        mod.CombatStyles.Add(new CombatStyle(mod));
        mod.MovementTypes.Add(new MovementType(mod) { MovementData = new MovementData() });
        mod.Explosions.Add(new Explosion(mod));
        var check = new ManifestOffsetCheck(Manifest());
        foreach (var positions in WrittenPositions(mod))
        {
            check.Check(positions, positions.Record.GetType().Name);
        }
        check.Wrong.ShouldBeEmpty(check.Report());
        check.Unread.ShouldBeEmpty(check.Report());
        foreach (var held in new[]
                 {
                     "Weapon.MeleeOrCreature", "Weapon.Equip", "Weapon.FastEquip", "MovementData.MaxSpeedForward",
                     "MovementData.MaxSpeedLeft", "MovementData.MaxRotationSpeedYaw", "MovementSpeeds.Run",
                     "CombatStyle.SpaceGeneralPowerWeighting", "Explosion.Sound1", "Explosion.Sound2",
                 })
        {
            check.Held.ShouldContainKey(held, check.Report());
        }
    }
}
