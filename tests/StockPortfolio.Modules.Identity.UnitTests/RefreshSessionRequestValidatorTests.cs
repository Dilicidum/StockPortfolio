using Shouldly;
using StockPortfolio.Modules.Identity.Api.Requests;
using StockPortfolio.Modules.Identity.Api.Validators;

namespace StockPortfolio.Modules.Identity.UnitTests;

/// <summary>Refresh validates presence and a plausible size, and nothing else.</summary>
public sealed class RefreshSessionRequestValidatorTests
{
    private readonly RefreshSessionRequestValidator _validator = new();

    /// <summary>Asserted on the error code, not the message: message copy is not a contract.</summary>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Validate_MissingToken_FailsNamingTheRefreshTokenField(string? refreshToken)
    {
        var result = _validator.Validate(new RefreshSessionRequest(refreshToken!));

        result.IsValid.ShouldBeFalse();

        var failure = result.Errors.ShouldHaveSingleItem();
        failure.PropertyName.ShouldBe(nameof(RefreshSessionRequest.RefreshToken));
        failure.ErrorCode.ShouldBe("NotEmptyValidator");
    }

    [Fact]
    public void Validate_TokenAtTheMaximumLength_Succeeds()
    {
        var atTheLimit = new string('t', RefreshSessionRequestValidator.MaximumRefreshTokenLength);

        var result = _validator.Validate(new RefreshSessionRequest(atTheLimit));

        result.IsValid.ShouldBeTrue(
            "The boundary is inclusive. An off-by-one here rejects the longest token the issuer can mint.");
        result.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Validate_TokenOneCharacterOverTheMaximum_FailsNamingTheRefreshTokenField()
    {
        var overTheLimit = new string('t', RefreshSessionRequestValidator.MaximumRefreshTokenLength + 1);

        var result = _validator.Validate(new RefreshSessionRequest(overTheLimit));

        result.IsValid.ShouldBeFalse();

        var failure = result.Errors.ShouldHaveSingleItem();
        failure.PropertyName.ShouldBe(nameof(RefreshSessionRequest.RefreshToken));
        failure.ErrorCode.ShouldBe("MaximumLengthValidator");
    }
}
