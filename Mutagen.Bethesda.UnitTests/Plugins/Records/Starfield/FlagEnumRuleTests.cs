using System.Reflection;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Noggog;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C71: a [Flags] enum that names bit 31 is uint. An int member at 0x8000_0000 is negative; Noggog's
// EnumerateContainedFlags (Noggog.CSharpExt 4.3.0, used by the JSON/YAML serializer's flag writer) widens it to
// 0xFFFFFFFF_80000000 and its `while (flag < num) flag <<= 1;` overflows to 0, so it never returns, for any value of
// such an enum. NavigationMesh.MajorFlag and BiomeMarker.MajorFlag (C55) had that member; both are uint now.
public class FlagEnumRuleTests
{
    private static readonly Assembly[] Assemblies =
    {
        typeof(StarfieldMod).Assembly,
        typeof(IMajorRecordGetter).Assembly,
        typeof(GameRelease).Assembly,
    };

    private static IEnumerable<Type> FlagEnums() => Assemblies
        .SelectMany(a => a.GetTypes())
        .Where(t => t.IsEnum && !t.ContainsGenericParameters && t.GetCustomAttribute<FlagsAttribute>() != null)
        .Distinct();

    [Fact]
    public void FlagEnums_HaveNoNegativeMember_AndNameBit31OnlyWhenUnsigned()
    {
        var bad = new List<string>();
        foreach (var t in FlagEnums())
        {
            var unsigned = Enum.GetUnderlyingType(t) is var u && (u == typeof(byte) || u == typeof(ushort) || u == typeof(uint) || u == typeof(ulong));
            if (unsigned) continue;
            foreach (var v in Enum.GetValues(t))
            {
                if (Convert.ToInt64(v) < 0) bad.Add($"{t.FullName}.{v}");
            }
        }
        bad.ShouldBeEmpty();
    }

    private static readonly MethodInfo EnumerateContainedFlags = typeof(EnumExt).GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(m => m.Name == nameof(EnumExt.EnumerateContainedFlags) && m.IsGenericMethodDefinition && m.GetParameters().Length == 2);

    // Every Starfield MajorFlag enum (and every other [Flags] enum in Starfield, Core and Kernel): Noggog's
    // EnumerateContainedFlags returns, for each named member and for all bits set, with undefined bits included or not.
    [Fact]
    public void EnumerateContainedFlags_Returns()
    {
        var enums = FlagEnums().ToArray();
        // Every header-flag enum is covered (59 MajorFlag enums: the registry's and PlacedObject's eight)
        StarfieldMajorFlagRegistry.EnumTypesByRecordType.Values.ShouldAllBe(e => enums.Contains(e));
        enums.Count(t => t.Name.EndsWith("MajorFlag")).ShouldBe(59);
        var hung = new List<string>();
        foreach (var t in enums)
        {
            var method = EnumerateContainedFlags.MakeGenericMethod(t);
            var values = Enum.GetValues(t).Cast<object>().Append(Enum.ToObject(t, -1L)).ToArray();
            var task = Task.Run(() =>
            {
                foreach (var v in values)
                {
                    foreach (var undefined in new[] { false, true })
                    {
                        foreach (var _ in (System.Collections.IEnumerable)method.Invoke(null, new[] { v, undefined })!) { }
                    }
                }
            });
            if (!task.Wait(TimeSpan.FromSeconds(10))) hung.Add(t.FullName!);
        }
        hung.ShouldBeEmpty();
    }

    [Fact]
    public void Bit31_IsEnumerated()
    {
        (NavigationMesh.MajorFlag.NavmeshGenCell | NavigationMesh.MajorFlag.Autogen).EnumerateContainedFlags()
            .ShouldBe(new[] { NavigationMesh.MajorFlag.Autogen, NavigationMesh.MajorFlag.NavmeshGenCell }, ignoreOrder: true);
        BiomeMarker.MajorFlag.MustBeUnique.EnumerateContainedFlags(includeUndefined: true)
            .ShouldBe(new[] { BiomeMarker.MajorFlag.MustBeUnique });
    }
}
