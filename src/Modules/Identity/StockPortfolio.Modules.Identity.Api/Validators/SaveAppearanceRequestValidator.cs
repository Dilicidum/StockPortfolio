using FluentValidation;

using StockPortfolio.Modules.Identity.Api.Requests;

namespace StockPortfolio.Modules.Identity.Api.Validators;

public sealed class SaveAppearanceRequestValidator : AbstractValidator<SaveAppearanceRequest>
{
    public static readonly string[] Themes = ["light", "dark", "system"];
    public static readonly string[] Languages = ["en", "uk"];

    public SaveAppearanceRequestValidator()
    {
        RuleFor(r => r.Theme).Cascade(CascadeMode.Stop)
            .NotEmpty().WithErrorCode("theme.required")
            .Must(Themes.Contains!).WithErrorCode("theme.unknown");

        RuleFor(r => r.Language).Cascade(CascadeMode.Stop)
            .NotEmpty().WithErrorCode("language.required")
            .Must(Languages.Contains!).WithErrorCode("language.unknown");
    }
}
