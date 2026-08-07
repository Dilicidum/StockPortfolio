using FluentValidation;

using StockPortfolio.Modules.Alerts.Api.Requests;

namespace StockPortfolio.Modules.Alerts.Api.Validators;

public sealed class SaveAlertSettingRequestValidator : AbstractValidator<SaveAlertSettingRequest>
{
    public const decimal MaximumPercent = 100m;

    public SaveAlertSettingRequestValidator()
    {
        RuleFor(request => request.Ticker)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("A ticker is required.")
            .Matches("^[A-Za-z]{1,5}$")
            .WithMessage("A ticker is 1 to 5 letters, A to Z.");

        RuleFor(request => request.ThresholdPercent)
            .Cascade(CascadeMode.Stop)
            .GreaterThan(0m)
            .WithMessage("A threshold must be greater than zero.")
            .LessThanOrEqualTo(MaximumPercent)
            .WithMessage("A threshold must be at most 100 percent.");

        RuleFor(request => request.WindowMinutes)
            .GreaterThanOrEqualTo(1)
            .WithMessage("A window is at least one minute.");
    }
}
