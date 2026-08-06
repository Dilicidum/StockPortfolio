using FluentValidation;

using StockPortfolio.Modules.Portfolio.Api.Requests;
using StockPortfolio.Modules.Portfolio.Domain;

namespace StockPortfolio.Modules.Portfolio.Api.Validators;

// Shape rules for SaveDashboardSettingsRequest. The range is repeated from RefreshInterval only as a
// fast edge rejection; RefreshInterval.Create is where the range actually lives.
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
