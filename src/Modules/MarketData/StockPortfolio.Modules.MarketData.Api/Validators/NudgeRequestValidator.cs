using FluentValidation;

using StockPortfolio.Modules.MarketData.Api.Requests;

namespace StockPortfolio.Modules.MarketData.Api.Validators;

public sealed class NudgeRequestValidator : AbstractValidator<NudgeRequest>
{
    public const decimal MinimumPercent = -99m;

    public const decimal MaximumPercent = 900m;

    public const int MaximumTtlSeconds = 3600;

    public NudgeRequestValidator()
    {
        RuleFor(request => request.Ticker)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("A ticker is required.")
            .Matches("^[A-Za-z]{1,5}$")
            .WithMessage("A ticker is 1 to 5 letters, A to Z.");

        RuleFor(request => request.Percent)
            .InclusiveBetween(MinimumPercent, MaximumPercent)
            .WithMessage($"A nudge is between {MinimumPercent} and {MaximumPercent} percent.");

        RuleFor(request => request.TtlSeconds)
            .InclusiveBetween(1, MaximumTtlSeconds)
            .WithMessage($"A nudge lasts between 1 and {MaximumTtlSeconds} seconds.");
    }
}
