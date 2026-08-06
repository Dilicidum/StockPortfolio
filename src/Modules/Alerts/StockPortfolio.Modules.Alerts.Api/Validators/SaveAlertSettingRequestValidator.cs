using FluentValidation;

using StockPortfolio.Modules.Alerts.Api.Requests;

namespace StockPortfolio.Modules.Alerts.Api.Validators;

/// <summary>Shape rules for SaveAlertSettingRequest — is this even a ticker, a percentage, a window? No I/O.</summary>
public sealed class SaveAlertSettingRequestValidator : AbstractValidator<SaveAlertSettingRequest>
{
    /// <summary>The largest move worth asking about: a total loss is one hundred percent.</summary>
    public const decimal MaximumPercent = 100m;

    /// <summary>Builds the rule set.</summary>
    public SaveAlertSettingRequestValidator()
    {
        // Either case is accepted and Ticker.Create upper-cases: rejecting "aapl" would be rejecting a
        // correct request for looking untidy.
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

        // No upper bound here on purpose: the cap is configuration, so the handler owns it and answers
        // 409 naming both numbers. A shape rule cannot see a value it would have to be rebuilt to know.
        RuleFor(request => request.WindowMinutes)
            .GreaterThanOrEqualTo(1)
            .WithMessage("A window is at least one minute.");
    }
}
