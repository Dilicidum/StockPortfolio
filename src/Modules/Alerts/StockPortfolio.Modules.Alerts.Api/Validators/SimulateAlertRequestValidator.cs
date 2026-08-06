using FluentValidation;

using StockPortfolio.Modules.Alerts.Api.Requests;

namespace StockPortfolio.Modules.Alerts.Api.Validators;

/// <summary>Shape rules for SimulateAlertRequest. The one field is optional, so most bodies say nothing.</summary>
public sealed class SimulateAlertRequestValidator : AbstractValidator<SimulateAlertRequest>
{
    /// <summary>Builds the rule set.</summary>
    public SimulateAlertRequestValidator()
    {
        // A null ticker is the ordinary request - the client always sends {"ticker": null}, because a
        // bodiless POST 415s against a required parameter. Only a value that is present and not a
        // ticker is a shape failure; "you have no threshold on it" is the handler's 409.
        RuleFor(request => request.Ticker!)
            .Matches("^[A-Za-z]{1,5}$")
            .WithMessage("A ticker is 1 to 5 letters, A to Z.")
            .When(request => request.Ticker is not null);
    }
}
