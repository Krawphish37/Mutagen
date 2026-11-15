using Shouldly;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Skyrim;
using Xunit;

namespace Mutagen.Bethesda.Tests.Plugins.Exceptions;

public class RecordExceptionTests
{
    [Fact]
    public void EnrichWithUmbrellaInterface()
    {
        var ex = new Exception();
        var rec = RecordException.Enrich<IItem>(ex, FormKey.Null, "SomeEdid");
        rec.RecordType.ShouldBe(typeof(IItem));
    }
        
    [Fact]
    public void CreateWithUmbrellaInterface()
    {
        var ex = new Exception();
        var rec = RecordException.Create<IItem>("Message", FormKey.Null, "SomeEdid");
        rec.RecordType.ShouldBe(typeof(IItem));
    }

    // C41: the record class comes from Loqui's registrations, warmed up where they are looked up, not in a static
    // constructor of the exception
    [Fact]
    public void EnrichAndCreateWithRecordInterfaceGiveItsClass()
    {
        RecordException.Enrich<IWeaponGetter>(new Exception(), FormKey.Null, "SomeEdid").RecordType.ShouldBe(typeof(Weapon));
        RecordException.Create<IWeaponGetter>("Message", FormKey.Null, "SomeEdid").RecordType.ShouldBe(typeof(Weapon));
    }
}