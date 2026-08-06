using Microsoft.Extensions.Time.Testing;

using Shouldly;

using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Tests;

public sealed class UserProviderKeyTests
{
    private static readonly Guid User = Guid.CreateVersion7();

    private static readonly DateTimeOffset Now = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_StampsSavedAt_AndLeavesLastRejectedAtNull()
    {
        var clock = new FakeTimeProvider(Now);

        var key = UserProviderKey.Create(User, "cipher", "1234", clock);

        key.UserId.ShouldBe(User);
        key.Ciphertext.ShouldBe("cipher");
        key.LastFour.ShouldBe("1234");
        key.SavedAt.ShouldBe(Now);
        key.LastRejectedAt.ShouldBeNull();
    }

    [Fact]
    public void Replace_OverwritesTheCiphertext_AndClearsAnyEarlierRejection()
    {
        var clock = new FakeTimeProvider(Now);
        var key = UserProviderKey.Create(User, "old-cipher", "1111", clock);

        key.MarkRejected(clock);

        clock.Advance(TimeSpan.FromMinutes(5));
        key.Replace("new-cipher", "2222", clock);

        key.Ciphertext.ShouldBe("new-cipher");
        key.LastFour.ShouldBe("2222");
        key.SavedAt.ShouldBe(Now + TimeSpan.FromMinutes(5));
        key.LastRejectedAt.ShouldBeNull();
    }

    [Fact]
    public void MarkRejected_StampsTheRejectionTime_WithoutTouchingTheCiphertext()
    {
        var clock = new FakeTimeProvider(Now);
        var key = UserProviderKey.Create(User, "cipher", "1234", clock);

        clock.Advance(TimeSpan.FromDays(1));
        key.MarkRejected(clock);

        key.Ciphertext.ShouldBe("cipher");
        key.LastRejectedAt.ShouldBe(Now + TimeSpan.FromDays(1));
    }
}
