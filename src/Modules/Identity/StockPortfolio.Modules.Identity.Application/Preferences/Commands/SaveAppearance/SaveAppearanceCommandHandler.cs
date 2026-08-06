using OneOf;

using StockPortfolio.Modules.Identity.Application.Abstractions;
using StockPortfolio.Modules.Identity.Application.Preferences.Queries.GetAppearance;
using StockPortfolio.Modules.Identity.Domain;
using StockPortfolio.Shared.Kernel;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Identity.Application.Preferences.Commands.SaveAppearance;

public sealed class SaveAppearanceCommandHandler(IUserPreferencesRepository repository)
    : ICommandHandler<SaveAppearanceCommand, OneOf<GetAppearanceResult, InvalidInput>>
{
    public async Task<OneOf<GetAppearanceResult, InvalidInput>> Handle(
        SaveAppearanceCommand command, CancellationToken ct)
    {
        if (!Wire.TryParseTheme(command.Theme, out var theme))
        {
            return new InvalidInput("theme", "Theme must be light, dark or system.");
        }

        if (!Wire.TryParseLanguage(command.Language, out var language))
        {
            return new InvalidInput("language", "Language must be en or uk.");
        }

        var userId = new UserId(command.UserId);
        var preferences = await repository.FindAsync(userId, ct) ?? UserPreferences.CreateDefault(userId);
        preferences.ChangeAppearance(theme, language);
        await repository.SaveAsync(preferences, ct);

        return new GetAppearanceResult(Wire.Theme(theme), Wire.Language(language));
    }
}
