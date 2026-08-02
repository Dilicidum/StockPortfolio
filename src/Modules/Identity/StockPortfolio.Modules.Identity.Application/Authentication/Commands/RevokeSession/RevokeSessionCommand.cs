namespace StockPortfolio.Modules.Identity.Application.Authentication.Commands.RevokeSession;

/// <summary>End a session — log out.</summary>
public sealed record RevokeSessionCommand(string RefreshToken);
