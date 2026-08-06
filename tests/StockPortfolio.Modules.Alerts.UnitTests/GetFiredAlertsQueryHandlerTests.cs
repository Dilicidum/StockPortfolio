using Shouldly;

using StockPortfolio.Modules.Alerts.Application;
using StockPortfolio.Modules.Alerts.Application.History.Queries.GetFiredAlerts;
using StockPortfolio.Modules.Alerts.Application.Streaming;
using StockPortfolio.Modules.Alerts.Domain;
using StockPortfolio.Shared.Kernel;
using StockPortfolio.Tests.Fakes;

namespace StockPortfolio.Tests;

/// <summary>The limit is a clamp, not a rule, and the row the panel renders is built here.</summary>
public sealed class GetFiredAlertsQueryHandlerTests
{
    private const int HistoryLimit = 50;

    private static readonly DateTimeOffset Now = new(2026, 8, 6, 14, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly List<string> _journal = [];
    private readonly FakeFiredAlertRepository _alerts;
    private readonly GetFiredAlertsQueryHandler _handler;

    /// <summary>Creates the handler over an in-memory table.</summary>
    public GetFiredAlertsQueryHandlerTests()
    {
        _alerts = new FakeFiredAlertRepository(_journal);

        _handler = new GetFiredAlertsQueryHandler(
            _alerts,
            new AlertsOptions(
                AlertsOptions.DefaultMaxWindowMinutes,
                TimeSpan.FromMinutes(AlertsOptions.DefaultCooldownMinutes),
                HistoryLimit,
                AlertsOptions.DefaultMinimumSamples,
                TimeSpan.FromMinutes(3)));
    }

    /// <summary>A limit over the server's ceiling comes back at the ceiling, not as a 400 and not as all of them.</summary>
    [Fact]
    public async Task ALimitOverTheCeiling_IsClampedToIt()
    {
        await SeedAsync(HistoryLimit + 10);

        var rows = await _handler.Handle(
            new GetFiredAlertsQuery(UserId, 100_000),
            TestContext.Current.CancellationToken);

        rows.Count.ShouldBe(HistoryLimit);
    }

    /// <summary>Zero and negative are somebody asking badly for a list, so they get the smallest one.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-7)]
    public async Task ALimitBelowOne_IsClampedToOne(int limit)
    {
        await SeedAsync(3);

        var rows = await _handler.Handle(
            new GetFiredAlertsQuery(UserId, limit),
            TestContext.Current.CancellationToken);

        rows.Count.ShouldBe(1);
    }

    /// <summary>The rendered row: two decimals on the percentages, and the sentence that names the comparison.</summary>
    [Fact]
    public async Task ARow_CarriesFormattedPercentagesAndTheReasonTheStreamWouldHavePushed()
    {
        await SeedAsync(1);

        var row = (await _handler.Handle(
            new GetFiredAlertsQuery(UserId, HistoryLimit),
            TestContext.Current.CancellationToken)).ShouldHaveSingleItem();

        row.Direction.ShouldBe(AlertDirection.Fall);
        row.ChangePercent.ShouldBe("-6.00");
        row.EndpointPercent.ShouldBe("-6.00");
        row.TriggerPrice.Amount.ShouldBe(141m);
        row.IsSimulated.ShouldBeFalse();

        // The same sentence the pushed frame carries, because both come out of MoveAssessment.Describe.
        var pushed = AlertNotification.From(Recorded(Now));

        row.Reason.ShouldBe(
            pushed.Reason,
            "a fetched row and a pushed one describe the same event. Two spellings would show up only "
                + "on rows that arrived live, which is the hardest possible thing to notice.");
        row.ChangePercent.ShouldBe(pushed.ChangePercent);
    }

    private static FiredAlert Recorded(DateTimeOffset firedAt) => FiredAlert.Record(
        UserId,
        Ticker.Create("AAPL").AsT0,
        AlertDirection.Fall,
        changePercent: -6m,
        endpointPercent: -6m,
        Money.Usd(141m),
        Money.Usd(150m),
        firedAt,
        isSimulated: false);

    private async Task SeedAsync(int count)
    {
        for (var index = 0; index < count; index++)
        {
            await _alerts.AddAsync(
                Recorded(Now.AddMinutes(-index)),
                TestContext.Current.CancellationToken);
        }
    }
}
