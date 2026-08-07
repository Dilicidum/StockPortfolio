using FluentValidation;

using StockPortfolio.Modules.Alerts.Api.Requests;

namespace StockPortfolio.Modules.Alerts.Api.Validators;

public sealed class SimulateAlertRequestValidator : AbstractValidator<SimulateAlertRequest>
{
    public SimulateAlertRequestValidator()
    {
        RuleFor(request => request.Ticker!)
            .Matches("^[A-Za-z]{1,5}$")
            .WithMessage("A ticker is 1 to 5 letters, A to Z.")
            .When(request => request.Ticker is not null);
    }
}
