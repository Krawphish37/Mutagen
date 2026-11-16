using System.Buffers.Binary;
using System.IO.Abstractions.TestingHelpers;
using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records;

// C83: values hand-written readers set get their own positions: a package data input's key (UNAM) and a target
// input's Type (ANAM text), a package target's fields (PTDA), a perk effect's Modification (DATA 1)
public class ValuePositionHandReadTests
{
    private static readonly ModKey StarfieldEsm = ModKey.FromFileName("Starfield.esm");
    private static readonly ModKey Plugin = ModKey.FromFileName("HandRead.esm");

    private static LoadOrder<IModMasterStyledGetter> Styles() => new(new IModMasterStyledGetter[]
    {
        new KeyedMasterStyle(StarfieldEsm, MasterStyle.Full),
        new KeyedMasterStyle(Plugin, MasterStyle.Full),
    });

    private static BinaryReadParameters Params(MockFileSystem fs) => new()
    {
        FileSystem = fs,
        MasterFlagsLookup = Styles(),
    };

    private static (MockFileSystem Fs, string Path) Write(StarfieldMod mod)
    {
        var fs = new MockFileSystem();
        var path = System.IO.Path.Combine("C:", "Data", Plugin.FileName);
        fs.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        mod.WriteToBinary(path, new BinaryWriteParameters { FileSystem = fs, MasterFlagsLookup = Styles() });
        return (fs, path);
    }

    private static ValuePosition One(RecordValuePositions positions, string path, ValuePositionKind kind = ValuePositionKind.Value)
    {
        var found = positions[path].Where(p => p.Kind == kind).ToList();
        found.Count.ShouldBe(1, $"{path} {kind}: {string.Join(" | ", positions.Positions.Select(p => $"{p.Path} {p.Kind}"))}");
        return found[0];
    }

    private static int Int(RecordValuePositions positions, ValuePosition p) =>
        p.Length == 1 ? (sbyte)positions.Slice(p).Span[0] : BinaryPrimitives.ReadInt32LittleEndian(positions.Slice(p).Span);

    [Fact]
    public void Package_InputKeys_TargetTypes_TargetFields()
    {
        var mod = new StarfieldMod(Plugin, StarfieldRelease.Starfield);
        var pack = mod.Packages.AddNew("Pack");
        pack.Data[0] = new PackageDataTarget
        {
            Type = PackageDataTarget.Types.SingleRef,
            Target = new PackageTargetSpecificReference
            {
                Reference = new FormLink<IPlacedGetter>(new FormKey(StarfieldEsm, 0xABC)),
                CountOrDistance = 3,
            },
        };
        pack.Data[1] = new PackageDataBool { Data = true, Name = "Flag" };
        pack.Data[2] = new PackageDataTarget
        {
            Type = PackageDataTarget.Types.Target,
            Target = new PackageTargetUnknown { Type = 99, Data = 7, CountOrDistance = -1 },
        };
        pack.Data[3] = new PackageDataTarget
        {
            Type = PackageDataTarget.Types.Target,
            Target = new PackageTargetObjectType { Type = TargetObjectType.Activators, CountOrDistance = 5 },
        };
        var (fs, path) = Write(mod);
        using var overlay = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield, Params(fs));
        var positions = overlay.Packages.Single().GetValuePositions();
        positions.Unplaced.ShouldBeEmpty();

        // Each input's key: its UNAM byte
        foreach (sbyte key in new sbyte[] { 0, 1, 2, 3 })
        {
            var p = One(positions, $"Data[{key}]", ValuePositionKind.Key);
            (p.Subrecord?.Type, p.Length, p.Offset).ShouldBe(("UNAM", 1, 0));
            Int(positions, p).ShouldBe(key);
        }

        // A target input's Type: its ANAM text, converted
        var type0 = One(positions, "Data[0].Type");
        (type0.Subrecord?.Type, type0.Converted).ShouldBe(("ANAM", true));
        Encoding.ASCII.GetString(positions.Slice(type0).Span).TrimEnd('\0').ShouldBe("SingleRef");
        Encoding.ASCII.GetString(positions.Slice(One(positions, "Data[2].Type")).Span).TrimEnd('\0').ShouldBe("TargetSelector");
        positions["Data[1].Type"].ShouldBeEmpty();

        // A target's fields, each at its place in PTDA
        One(positions, "Data[0].Target", ValuePositionKind.Object).Length.ShouldBe(12);
        var reference = One(positions, "Data[0].Target.Reference");
        (reference.Subrecord?.Type, reference.Offset, reference.Length, reference.Converted).ShouldBe(("PTDA", 4, 4, false));
        BinaryPrimitives.ReadUInt32LittleEndian(positions.Slice(reference).Span).ShouldBe(0x00000ABCu);
        var count = One(positions, "Data[0].Target.CountOrDistance");
        (count.Offset, count.Length).ShouldBe((8, 4));
        Int(positions, count).ShouldBe(3);
        var unknownType = One(positions, "Data[2].Target.Type");
        (unknownType.Offset, unknownType.Length).ShouldBe((0, 4));
        Int(positions, unknownType).ShouldBe(99);
        Int(positions, One(positions, "Data[2].Target.Data")).ShouldBe(7);
        Int(positions, One(positions, "Data[2].Target.CountOrDistance")).ShouldBe(-1);
        var objectType = One(positions, "Data[3].Target.Type");
        (objectType.Offset, objectType.Length).ShouldBe((4, 4));
        Int(positions, objectType).ShouldBe((int)TargetObjectType.Activators);
        Int(positions, One(positions, "Data[3].Target.CountOrDistance")).ShouldBe(5);
    }

    [Fact]
    public void Perk_Modification_FromTheFunction()
    {
        var mod = new StarfieldMod(Plugin, StarfieldRelease.Starfield);
        var perk = mod.Perks.AddNew("Perk");
        var rank = new PerkRank();
        rank.Effects.Add(new PerkEntryPointModifyValue
        {
            EntryPoint = APerkEntryPointEffect.EntryType.ModBreathTimer,
            Modification = PerkEntryPointModifyValue.ModificationType.Multiply,
            Value = 2f,
        });
        rank.Effects.Add(new PerkEntryPointModifyValues
        {
            EntryPoint = APerkEntryPointEffect.EntryType.ModBreathTimer,
            Modification = PerkEntryPointModifyValue.ModificationType.Add,
            Value = 1f,
            Value2 = 2f,
        });
        rank.Effects.Add(new PerkEntryPointModifyActorValue
        {
            EntryPoint = APerkEntryPointEffect.EntryType.ModBreathTimer,
            Modification = PerkEntryPointModifyActorValue.ModificationType.MultiplyAVMult,
            ActorValue = new FormLink<IActorValueInformationGetter>(new FormKey(StarfieldEsm, 0x2D4)),
            Value = 0.5f,
        });
        rank.Effects.Add(new PerkEntryPointAbsoluteValue
        {
            EntryPoint = APerkEntryPointEffect.EntryType.ModBreathTimer,
            Negative = true,
        });
        perk.Ranks.Add(rank);
        var (fs, path) = Write(mod);
        using var overlay = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield, Params(fs));
        var positions = overlay.Perks.Single().GetValuePositions();
        positions.Unplaced.ShouldBeEmpty();

        var expected = new (string Path, APerkEntryPointEffect.FunctionType Function)[]
        {
            ("Ranks[0].Effects[0].Modification", APerkEntryPointEffect.FunctionType.MultiplyValue),
            ("Ranks[0].Effects[1].Modification", APerkEntryPointEffect.FunctionType.AddValue),
            ("Ranks[0].Effects[2].Modification", APerkEntryPointEffect.FunctionType.MultiplyActorValueMult),
            ("Ranks[0].Effects[3].Negative", APerkEntryPointEffect.FunctionType.NegativeAbsoluteValue),
        };
        foreach (var (p, function) in expected)
        {
            var at = One(positions, p);
            (at.Subrecord?.Type, at.Offset, at.Length, at.Converted).ShouldBe(("DATA", 1, 1, true), p);
            positions.Slice(at).Span[0].ShouldBe((byte)function, p);
        }
    }
}
