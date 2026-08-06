using StockPortfolio.Modules.Identity.Application.Abstractions;
using StockPortfolio.Modules.Identity.Application.Preferences.Commands.SaveAppearance;
using StockPortfolio.Modules.Identity.Domain;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Identity.Application.Preferences.Queries.GetAppearance;

public sealed class GetAppearanceQueryHandler(IUserPreferencesRepository repository)
    : IQueryHandler<GetAppearanceQuery, GetAppearanceResult>
{
    public async Task<GetAppearanceResult> Handle(GetAppearanceQuery query, CancellationToken ct)
    {
        var userId = new UserId(query.UserId);
        var preferences = await repository.FindAsync(userId, ct) ?? UserPreferences.CreateDefault(userId);

        return new GetAppearanceResult(Wire.Theme(preferences.Theme), Wire.Language(preferences.Language));
    }
}
