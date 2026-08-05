using FluentValidation;

using StockPortfolio.Modules.MarketData.Api.Requests;

namespace StockPortfolio.Modules.MarketData.Api.Validators;

/// <summary>Shape rules for NudgeRequest. No I/O — whether the symbol exists is not this layer's question.</summary>
public sealed class NudgeRequestValidator : AbstractValidator<NudgeRequest>
{
    /// <summary>A nudge cannot take a price to zero or below.</summary>
    public const decimal MinimumPercent = -99m;

    /// <summary>Ten times the price is already an absurd demo; beyond it is a typo.</summary>
    public const decimal MaximumPercent = 900m;

    /// <summary>An hour. A nudge that outlives the demo it was for is a lie about the fake's walk.</summary>
    public const int MaximumTtlSeconds = 3600;

    /// <summary>Builds the rule set.</summary>
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
