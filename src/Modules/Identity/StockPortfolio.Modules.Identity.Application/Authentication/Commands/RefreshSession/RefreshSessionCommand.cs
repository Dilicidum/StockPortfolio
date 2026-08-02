namespace StockPortfolio.Modules.Identity.Application.Authentication.Commands.RefreshSession;

/// <summary>Trade a refresh token for a fresh token pair.</summary>
public sealed record RefreshSessionCommand(string RefreshToken);
