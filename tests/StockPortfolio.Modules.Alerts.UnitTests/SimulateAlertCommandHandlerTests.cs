using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Shouldly;

using StockPortfolio.Modules.Alerts.Application;
using StockPortfolio.Modules.Alerts.Application.Simulation.Commands.SimulateAlert;
using StockPortfolio.Modules.Alerts.Application.Streaming;
using StockPortfolio.Modules.Alerts.Domain;
using StockPortfolio.Modules.MarketData.Contracts;
using StockPortfolio.Tests.Fakes;

namespace StockPortfolio.Tests;

/// <summary>Simulate goes down the real path, so what arrives proves the mechanism, not the button.</summary>
public sealed class SimulateAlertCommandHandlerTests
{
    private const decimal Threshold = 5m;
    private const int WindowMinutes = 30;

    private static readonly DateTimeOffset Now = new(2026, 8, 6, 14, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly List<string> _journal = [];
    private readonly FakeAlertSettingRepository _settings = new();
    private readonly FakePriceWindowReader _windows = new();
    private readonly FakeFiredAlertRepository _firedAlerts;
    private readonly FakeAlertPublisher _publisher;
    private readonly SimulateAlertCommandHandler _handler;

    /// <summary>Wires the handler over in-memory fakes.</summary>
    public SimulateAlertCommandHandlerTests()
    {
        _firedAlerts = new FakeFiredAlertRepository(_journal);
        _publisher = new FakeAlertPublisher(_journal);

        _handler = new SimulateAlertCommandHandler(
            _settings,
            _windows,
            new AlertDispatcher(_firedAlerts, _publisher, NullLogger<AlertDispatcher>.Instance),
            _clock);
    }

    /// <summary>With no threshold anywhere there is nothing to simulate, and that is a 409, not a fake row.</summary>
    [Fact]
    public async Task WithNoEnabledThreshold_ThereIsNothingToSimulate()
    {
        var result = await _handler.Handle(
            new SimulateAlertCommand(UserId, Ticker: null),
            TestContext.Current.CancellationToken);

        result.IsT1.ShouldBeTrue();
        _firedAlerts.Rows.ShouldBeEmpty();
    }

    /// <summary>A threshold that is switched off is not a threshold to simulate.</summary>
    [Fact]
    public async Task ADisabledThreshold_IsNotChosen()
    {
        Watching("AAPL", enabled: false);

        var result = await _handler.Handle(
            new SimulateAlertCommand(UserId, Ticker: null),
            TestContext.Current.CancellationToken);

        result.IsT1.ShouldBeTrue();
    }

    /// <summary>Naming a ticker you have no threshold on says so, rather than quietly firing another.</summary>
    [Fact]
    public async Task ATickerWithNoThreshold_IsRefused_RatherThanSubstituted()
    {
        Watching("AAPL");

        var result = await _handler.Handle(
            new SimulateAlertCommand(UserId, "MSFT"),
            TestContext.Current.CancellationToken);

        result.IsT1.ShouldBeTrue();
        result.AsT1.Ticker.ShouldBe("MSFT");
        _firedAlerts.Rows.ShouldBeEmpty(
            "picking a different position because the named one had no threshold would make the "
                + "button look like it worked and report the wrong instrument.");
    }

    /// <summary>The row is badged, carries the threshold as its move, and is saved before it is pushed.</summary>
    [Fact]
    public async Task ASimulatedAlert_IsBadged_SavedThenPublished()
    {
        Watching("AAPL");

        _windows.Returning(WindowMinutes, WindowAt(190m));

        var result = await _handler.Handle(
            new SimulateAlertCommand(UserId, Ticker: null),
            TestContext.Current.CancellationToken);

        result.IsT0.ShouldBeTrue();

        var alert = _firedAlerts.Rows.ShouldHaveSingleItem();

        alert.IsSimulated.ShouldBeTrue("the panel badges simulated rows, and it reads this flag.");
        alert.Direction.ShouldBe(AlertDirection.Fall);
        alert.ChangePercent.ShouldBe(-Threshold);
        alert.TriggerPrice.Amount.ShouldBe(190m, "the live price, so the demo reads as the position.");
        alert.ReferencePrice.Amount.ShouldBe(200m, "190 is five percent below 200.");

        _journal.ShouldBe([FakeFiredAlertRepository.Saved, FakeAlertPublisher.Published]);
        _publisher.Sent.ShouldHaveSingleItem().IsSimulated.ShouldBeTrue();
    }

    /// <summary>A ticker with no samples yet still simulates: the button must work in the first minute.</summary>
    [Fact]
    public async Task ATickerWithNoSeriesYet_StillSimulates()
    {
        Watching("AAPL");

        var result = await _handler.Handle(
            new SimulateAlertCommand(UserId, Ticker: null),
            TestContext.Current.CancellationToken);

        result.IsT0.ShouldBeTrue();
        _firedAlerts.Rows.ShouldHaveSingleItem().TriggerPrice.Amount.ShouldBe(100m);
    }

    /// <summary>Pressed twice, two alerts: no cooldown claim, because the user asked for exactly two.</summary>
    [Fact]
    public async Task PressedTwiceInAMoment_ProducesTwoAlerts()
    {
        Watching("AAPL");

        for (var press = 0; press < 2; press++)
        {
            (await _handler.Handle(
                new SimulateAlertCommand(UserId, Ticker: null),
                TestContext.Current.CancellationToken)).IsT0.ShouldBeTrue();
        }

        _firedAlerts.Rows.Count.ShouldBe(
            2,
            "a button the user just pressed must not be swallowed by a cooldown window some real "
                + "evaluation opened - it would read as a broken button.");
    }

    /// <summary>Lower case in is still the caller's own position.</summary>
    [Fact]
    public async Task ANamedTicker_IsMatchedCanonically()
    {
        Watching("AAPL");

        var result = await _handler.Handle(
            new SimulateAlertCommand(UserId, "aapl"),
            TestContext.Current.CancellationToken);

        result.IsT0.ShouldBeTrue();
    }

    private static PriceWindow WindowAt(decimal current) => new(
        Ticker: "AAPL",
        Current: current,
        Oldest: current,
        Low: current,
        High: current,
        OldestAt: Now.AddMinutes(-WindowMinutes),
        NewestAt: Now,
        SampleCount: 30,
        LargestGap: TimeSpan.FromMinutes(1));

    private void Watching(string ticker, bool enabled = true)
    {
        if (!AlertSetting
                .Create(
                    UserId,
                    ticker,
                    Threshold,
                    WindowMinutes,
                    enabled,
                    AlertsOptions.DefaultMaxWindowMinutes)
                .TryPickT0(out var setting, out var invalid))
        {
            throw new InvalidOperationException(invalid.Message);
        }

        _settings.With(setting);
    }
}
