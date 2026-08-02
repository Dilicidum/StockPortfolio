namespace StockPortfolio.Modules.Identity.Application;

/// <summary>What a successful register, login or refresh hands back.</summary>
public sealed record TokenPair(string AccessToken, string RefreshToken, DateTimeOffset AccessExpiresAt);
