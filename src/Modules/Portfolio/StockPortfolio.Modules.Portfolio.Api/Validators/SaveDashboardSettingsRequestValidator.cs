using FluentValidation;

using StockPortfolio.Modules.Portfolio.Api.Requests;
using StockPortfolio.Modules.Portfolio.Domain;

namespace StockPortfolio.Modules.Portfolio.Api.Validators;

public sealed class SaveDashboardSettingsRequestValidator : AbstractValidator<SaveDashboardSettingsRequest>
{
    public SaveDashboardSettingsRequestValidator()
    {
        RuleFor(request => request.RefreshIntervalSeconds)
            .InclusiveBetween(RefreshInterval.Minimum, RefreshInterval.Maximum)
            .WithMessage(
                $"Refresh interval must be between {RefreshInterval.Minimum} and {RefreshInterval.Maximum} seconds.");
    }
}
