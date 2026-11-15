using System.Reflection;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C61 (SFSearch W16): StarfieldMajorFlagRegistry maps a record type (signature or getter type) to the enum that names
// its header flags, so a consumer doesn't look for a nested MajorFlag type by name.
public class StarfieldMajorFlagRegistryTests
{
    private static readonly Assembly Starfield = typeof(StarfieldMod).Assembly;

    [Theory]
    [InlineData("KEYM", typeof(Key.MajorFlag))]
    [InlineData("CONT", typeof(Container.MajorFlag))]
    [InlineData("STAT", typeof(Static.MajorFlag))]
    [InlineData("OMOD", typeof(AObjectModification.MajorFlag))]
    [InlineData("PGRE", typeof(APlacedTrap.MajorFlag))]
    [InlineData("PMIS", typeof(APlacedTrap.MajorFlag))]
    [InlineData("NAVM", typeof(NavigationMesh.MajorFlag))]
    [InlineData("STDT", typeof(Star.MajorFlag))]
    public void ByRecordType(string recordType, Type expected)
    {
        StarfieldMajorFlagRegistry.TryGetEnumType(new RecordType(recordType), out var enumType).ShouldBeTrue();
        enumType.ShouldBe(expected);
    }

    [Theory]
    [InlineData(typeof(Key))]
    [InlineData(typeof(IKey))]
    [InlineData(typeof(IKeyGetter))]
    public void ByClassSetterOrGetter(Type type)
    {
        StarfieldMajorFlagRegistry.TryGetEnumType(type, out var enumType).ShouldBeTrue();
        enumType.ShouldBe(typeof(Key.MajorFlag));
    }

    [Fact]
    public void Unlisted()
    {
        // REFR depends on the base object; GMST has no header-flag names
        StarfieldMajorFlagRegistry.TryGetEnumType(new RecordType("REFR"), out _).ShouldBeFalse();
        StarfieldMajorFlagRegistry.TryGetEnumType(new RecordType("GMST"), out _).ShouldBeFalse();
        StarfieldMajorFlagRegistry.TryGetEnumType(typeof(IGameSettingGetter), out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("STAT", typeof(PlacedObject.StaticMajorFlag))]
    [InlineData("TERM", typeof(PlacedObject.ContainerMajorFlag))]
    [InlineData("DOOR", typeof(PlacedObject.DoorMajorFlag))]
    [InlineData("LIGH", typeof(PlacedObject.LightMajorFlag))]
    [InlineData("MSTT", typeof(PlacedObject.MoveableStaticMajorFlag))]
    [InlineData("ADDN", typeof(PlacedObject.AddonNodeMajorFlag))]
    [InlineData("WEAP", typeof(PlacedObject.ItemMajorFlag))]
    [InlineData("NPC_", typeof(PlacedObject.DefaultMajorFlag))]
    public void PlacedObject_ByBaseType(string baseRecordType, Type expected)
    {
        StarfieldMajorFlagRegistry.GetPlacedObjectEnumType(new RecordType(baseRecordType)).ShouldBe(expected);
    }

    // Every listed getter's MajorFlags property has the listed enum type
    [Fact]
    public void Entries_MatchMajorFlagsProperty()
    {
        foreach (var (getter, enumType) in StarfieldMajorFlagRegistry.EnumTypesByGetterType)
        {
            var prop = getter.GetProperty("MajorFlags")
                ?? getter.GetInterfaces().Select(i => i.GetProperty("MajorFlags")).First(p => p != null)!;
            prop.PropertyType.ShouldBe(enumType, getter.Name);
        }
        StarfieldMajorFlagRegistry.EnumTypesByRecordType.Count.ShouldBe(StarfieldMajorFlagRegistry.EnumTypesByGetterType.Count);
    }

    // Every record class with a MajorFlags property is listed, under its own record type
    [Fact]
    public void Registry_IsComplete()
    {
        var classes = Starfield.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.IsPublic && typeof(IStarfieldMajorRecordGetter).IsAssignableFrom(t))
            .Where(t => t.GetProperty("MajorFlags", BindingFlags.Public | BindingFlags.Instance) is { PropertyType.IsEnum: true })
            .ToArray();
        classes.Length.ShouldBeGreaterThan(50);
        foreach (var cls in classes)
        {
            var enumType = cls.GetProperty("MajorFlags", BindingFlags.Public | BindingFlags.Instance)!.PropertyType;
            StarfieldMajorFlagRegistry.TryGetEnumType(cls, out var byType).ShouldBeTrue(cls.Name);
            byType.ShouldBe(enumType, cls.Name);
            var recordType = (RecordType)cls.GetField("GrupRecordType", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)!.GetValue(null)!;
            StarfieldMajorFlagRegistry.TryGetEnumType(recordType, out var bySig).ShouldBeTrue(cls.Name);
            bySig.ShouldBe(enumType, cls.Name);
        }
    }

    // No header-flag enum gives two names to one value (C56)
    [Fact]
    public void NoTwoNamesForOneValue()
    {
        var enums = StarfieldMajorFlagRegistry.EnumTypesByRecordType.Values
            .Concat(typeof(PlacedObject).GetNestedTypes().Where(t => t.IsEnum && t.Name.EndsWith("MajorFlag")))
            .Distinct();
        foreach (var e in enums)
        {
            var values = Enum.GetValues(e).Cast<object>().Select(v => Convert.ToInt64(v) & 0xFFFF_FFFF).ToArray();
            values.Distinct().Count().ShouldBe(values.Length, e.FullName);
        }
    }
}
