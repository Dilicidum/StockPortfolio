namespace StockPortfolio.Modules.Identity.Application.Preferences.Commands.SaveAppearance;

public sealed record SaveAppearanceCommand(Guid UserId, string Theme, string Language);
