namespace StockPortfolio.Modules.MarketData.Application.Keys.Queries.GetApiKeyStatus;

/// <summary>The key is never in here — not even masked beyond the last four. Every path that returns
/// it is a path that can leak it. Rejected is true when the provider refused this key on a real fetch.</summary>
public sealed record GetApiKeyStatusResult(bool Configured, string? LastFour, bool Rejected);
