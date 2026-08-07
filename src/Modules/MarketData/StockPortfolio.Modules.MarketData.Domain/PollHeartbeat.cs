namespace StockPortfolio.Modules.MarketData.Domain;

/// <summary>What one finished poll cycle leaves behind: when it ended, what it was asked for, what it stored.</summary>
public readonly record struct PollHeartbeat(DateTimeOffset At, int TickersTargeted, int TickersStored);
