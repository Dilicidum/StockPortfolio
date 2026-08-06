namespace StockPortfolio.Modules.Identity.Application.Preferences.Commands.SaveAppearance;

public sealed record SaveAppearanceCommand(string UserId, string Theme, string Language);
