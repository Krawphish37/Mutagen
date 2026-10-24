using Loqui;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Records.Mapping;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Mapping;

public class RecordInterfaceRegistrationTests
{
    public static TheoryData<GameCategory> Categories()
    {
        var ret = new TheoryData<GameCategory>();
        foreach (var cat in Enum.GetValues<GameCategory>())
        {
            ret.Add(cat);
        }
        return ret;
    }

    private static IMod NewMod(GameCategory category)
    {
        return ModInstantiator.Activator(ModKey.FromNameAndExtension("Test.esp"), category.DefaultRelease());
    }

    /// <summary>
    /// Every public interface a game assembly exposes for major records should be either a record's own
    /// interface or one of the registered interfaces (link, aspect, inheriting or isolated abstract).
    /// An interface that is neither compiles, but no record implements it, and every query throws.
    /// </summary>
    private static Type[] RecordInterfaces(GameCategory category)
    {
        return NewMod(category).GetType().Assembly.GetExportedTypes()
            .Where(t => t.IsInterface)
            .Where(t => !t.IsGenericTypeDefinition)
            .Where(t => typeof(IMajorRecordGetter).IsAssignableFrom(t))
            .Where(t => !LoquiRegistration.TryGetRegister(t, out _))
            .ToArray();
    }

    [Theory]
    [MemberData(nameof(Categories))]
    public void RecordInterfacesAreRegistered(GameCategory category)
    {
        var interfaces = RecordInterfaces(category);
        interfaces.ShouldNotBeEmpty();
        interfaces
            .Where(t => !MetaInterfaceMapping.Instance.TryGetRegistrationsForInterface(category, t, out _))
            .Select(t => t.FullName)
            .ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Categories))]
    public void RecordInterfacesCanBeEnumerated(GameCategory category)
    {
        var mod = NewMod(category);
        var failed = new List<string>();
        foreach (var t in RecordInterfaces(category))
        {
            try
            {
                if (typeof(IMajorRecord).IsAssignableFrom(t))
                {
                    ((IMajorRecordEnumerable)mod).EnumerateMajorRecords(t).ToArray();
                }
                else
                {
                    ((IMajorRecordGetterEnumerable)mod).EnumerateMajorRecords(t).ToArray();
                }
            }
            catch (Exception ex)
            {
                failed.Add($"{t.FullName}: {ex.Message}");
            }
        }
        failed.ShouldBeEmpty();
    }
}
