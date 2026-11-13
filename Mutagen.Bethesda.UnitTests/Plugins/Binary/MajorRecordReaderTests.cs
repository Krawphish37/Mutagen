using System.IO.Abstractions;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Analysis;
using Mutagen.Bethesda.Plugins.Binary;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Records.Mapping;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Testing.AutoData;
using Noggog;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Binary;

// C176: a public way to read one record at a known position of a plugin (SFSearch did it through reflection). Each record
// read on its own should equal the record of a full import; a holder (a cell) comes with or without its children.
public class MajorRecordReaderTests
{
    [Theory, MutagenAutoData]
    public async Task ReadAt_EqualsTheFullImport(IFileSystem fileSystem, DirectoryPath existingTempDir)
    {
        var mod = new StarfieldMod(new ModKey("ReadAt", ModType.Master), StarfieldRelease.Starfield);
        var keyword = mod.Keywords.AddNew("TestKeyword");
        var weapon = mod.Weapons.AddNew("TestWeapon");
        weapon.Name = "Test Weapon";
        weapon.Keywords = new() { keyword.ToLink() };
        var settingFloat = new GameSettingFloat(mod, "fTestSetting") { Data = 1.5f };
        var settingInt = new GameSettingInt(mod, "iTestSetting") { Data = 7 };
        mod.GameSettings.Add(settingFloat);
        mod.GameSettings.Add(settingInt);
        var cell = new Cell(mod) { EditorID = "TestCell" };
        var placed = new PlacedObject(mod) { EditorID = "TestPlaced" };
        placed.Base.SetTo(weapon);
        cell.Temporary.Add(placed);
        mod.Cells.Add(new CellBlock { BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellBlock, SubBlocks = { new CellSubBlock { BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellSubBlock, Cells = { cell } } } });
        var path = new ModPath(mod.ModKey, Path.Combine(existingTempDir, mod.ModKey.FileName));
        await mod.BeginWrite.ToPath(path).WithNoLoadOrder().NoModKeySync().WithFileSystem(fileSystem).WriteAsync();

        var imported = StarfieldMod.Create(StarfieldRelease.Starfield).FromPath(path).WithNoLoadOrder().WithFileSystem(fileSystem).Mutable().Construct();
        var locations = RecordLocator.GetLocations(path, GameRelease.Starfield, loadOrder: null, fileSystem: fileSystem);
        using var reader = MajorRecordReader.FromPath(path, GameRelease.Starfield, new BinaryReadParameters { FileSystem = fileSystem });

        foreach (var formKey in new[] { keyword.FormKey, weapon.FormKey, settingFloat.FormKey, settingInt.FormKey, placed.FormKey })
        {
            var read = reader.ReadAt(locations[formKey].Min);
            imported.EnumerateMajorRecords().Single(r => r.FormKey == formKey).Equals(read).ShouldBeTrue(formKey.ToString());
        }
        reader.ReadAt(locations[settingInt.FormKey].Min).ShouldBeOfType<GameSettingInt>().Data.ShouldBe(7);

        // A cell alone has no children; with them it is the cell of the full import
        var cellPosition = locations[cell.FormKey].Min;
        reader.ReadAt(cellPosition).ShouldBeOfType<Cell>().Temporary.ShouldBeEmpty();
        var withChildren = reader.ReadAt(cellPosition, withChildren: true).ShouldBeOfType<Cell>();
        withChildren.Temporary.Select(p => p.FormKey).ShouldBe(new[] { placed.FormKey });
        withChildren.Equals(imported.Cells.Records.Single().SubBlocks.Single().Cells.Single()).ShouldBeTrue();

        // The bytes form: what SFSearch reads through its own file windows
        reader.Read(reader.ReadBytesAt(locations[weapon.FormKey].Min)).ShouldBeOfType<Weapon>().Name!.String.ShouldBe("Test Weapon");

        // A group is not a record
        Should.Throw<ArgumentException>(() => reader.ReadAt(locations.GrupLocations.Keys.First()));
    }

    [Fact]
    public void GetRecordClass_IsTheClassTheFullImportReads()
    {
        MajorRecordReader.GetRecordClass(GameCategory.Starfield, new RecordType("GMST")).ShouldBe(typeof(GameSetting));
        MajorRecordReader.GetRecordClass(GameCategory.Starfield, new RecordType("WEAP")).ShouldBe(typeof(Weapon));
        MajorRecordReader.GetRecordClass(GameCategory.Starfield, new RecordType("OMOD")).ShouldBe(typeof(AObjectModification));
        MajorRecordReader.GetRecordClass(GameCategory.Starfield, new RecordType("REFR")).ShouldBe(typeof(PlacedObject));
        MajorRecordReader.GetRecordClass(GameCategory.Starfield, new RecordType("ZZZZ")).ShouldBeNull();

        // Every record type of a top-level group of each game can be read on its own
        var missing = new List<string>();
        foreach (var category in Enums<GameCategory>.Values)
        {
            foreach (var types in MajorRecordTypeEnumerator.GetTopLevelMajorRecordTypesFor(category))
            {
                var recordType = (RecordType)Loqui.LoquiRegistration.GetRegister(types.ClassType)!.GetType()
                    .GetField("TriggeringRecordType")!.GetValue(null)!;
                // The group's class, or its base class whose reader picks the class (GMST: GameSetting)
                var cls = MajorRecordReader.GetRecordClass(category, recordType);
                if (cls == null || !(types.ClassType == cls || types.ClassType.IsSubclassOf(cls)))
                {
                    missing.Add($"{category} {recordType} {types.ClassType.Name}: {cls?.Name ?? "none"}");
                }
            }
        }
        missing.ShouldBeEmpty(string.Join(Environment.NewLine, missing));
    }
}
