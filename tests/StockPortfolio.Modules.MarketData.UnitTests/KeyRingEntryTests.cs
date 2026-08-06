using Shouldly;

using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Tests;

public sealed class KeyRingEntryTests
{
    [Fact]
    public void Create_KeepsEveryValueItWasGiven()
    {
        var entry = KeyRingEntry.Create("key-1", "<key>xml</key>");

        entry.FriendlyName.ShouldBe("key-1");
        entry.Xml.ShouldBe("<key>xml</key>");
        entry.Id.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void Create_GivesEveryEntryItsOwnId() =>
        KeyRingEntry.Create("a", "<a/>").Id.ShouldNotBe(KeyRingEntry.Create("b", "<b/>").Id);
}
