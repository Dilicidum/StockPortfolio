using Shouldly;
using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.UnitTests;

/// <summary>
/// <see cref="UserId.New"/> must produce UUIDv7 values, because the whole reason the id is
/// generated in the domain rather than by the database is index locality — and an id that does not
/// sort by creation time silently gives that up while every other test still passes.
/// </summary>
public sealed class UserIdTests
{
    [Fact]
    public void New_Always_ProducesAVersion7Guid()
    {
        UserId.New().Value.Version.ShouldBe(7);
    }

    [Fact]
    public void New_CalledRepeatedly_ProducesDistinctValues()
    {
        var ids = Enumerable.Range(0, 1_000).Select(_ => UserId.New()).ToList();

        ids.Distinct().Count().ShouldBe(ids.Count);
    }

    [Fact]
    public void New_CalledInATightLoop_ProducesNonDecreasingTimestamps()
    {
        // Within a single millisecond the trailing bits are random, so consecutive ids are not
        // strictly ordered. The leading 48-bit timestamp is what the index cares about, and that
        // must never go backwards.
        var timestamps = Enumerable.Range(0, 500)
            .Select(_ => TimestampOf(UserId.New()))
            .ToList();

        timestamps.ShouldBe(timestamps.Order().ToList());
    }

    [Fact]
    public async Task New_CalledOverTime_ProducesAscendingValues()
    {
        var ids = new List<UserId>();

        for (var i = 0; i < 5; i++)
        {
            ids.Add(UserId.New());

            // Enough to move the millisecond counter, so the ordering below is deterministic and
            // does not depend on the random tail.
            await Task.Delay(TimeSpan.FromMilliseconds(4), TestContext.Current.CancellationToken);
        }

        // Sorting by the RFC 9562 byte order must return them to creation order. Hex text ordinal
        // comparison is that byte order; Guid.CompareTo is NOT — it compares the first field as a
        // signed Int32 and the next two as signed Int16, so it reorders v7 guids at sign
        // boundaries.
        var sorted = ids.OrderBy(id => id.Value.ToString("N"), StringComparer.Ordinal).ToList();

        sorted.ShouldBe(ids);
    }

    [Fact]
    public async Task New_CalledOverTime_ProducesStrictlyIncreasingTimestamps()
    {
        var first = TimestampOf(UserId.New());
        await Task.Delay(TimeSpan.FromMilliseconds(4), TestContext.Current.CancellationToken);
        var second = TimestampOf(UserId.New());

        second.ShouldBeGreaterThan(first);
    }

    [Fact]
    public void ToString_Always_ReturnsTheHyphenatedGuid()
    {
        var guid = Guid.CreateVersion7();

        new UserId(guid).ToString().ShouldBe(guid.ToString("D"));
    }

    /// <summary>Reads the leading 48-bit millisecond timestamp out of a UUIDv7.</summary>
    /// <param name="id">The id to read.</param>
    /// <returns>Milliseconds since the Unix epoch, as encoded in the id.</returns>
    private static long TimestampOf(UserId id)
    {
        Span<byte> bytes = stackalloc byte[16];

        if (!id.Value.TryWriteBytes(bytes, bigEndian: true, out _))
        {
            throw new InvalidOperationException("Could not read the bytes of the id.");
        }

        long milliseconds = 0;

        for (var i = 0; i < 6; i++)
        {
            milliseconds = (milliseconds << 8) | bytes[i];
        }

        return milliseconds;
    }
}
