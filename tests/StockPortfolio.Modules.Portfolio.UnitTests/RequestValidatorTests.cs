using Shouldly;
using StockPortfolio.Modules.Portfolio.Api.Requests;
using StockPortfolio.Modules.Portfolio.Api.Validators;

namespace StockPortfolio.Tests;

/// <summary>Shape validation, the layer that answers 400 before a handler ever runs.</summary>
public sealed class RequestValidatorTests
{
    private static readonly AddHoldingRequestValidator Add = new();
    private static readonly UpdateHoldingRequestValidator Update = new();

    [Theory]
    [InlineData("AAPL", 10, 100)]
    [InlineData("aapl", 0.000001, 0.01)]
    [InlineData("F", 1, 1)]
    public void Add_AcceptsAWellFormedPurchase(string ticker, decimal quantity, decimal price) =>
        Add.Validate(new AddHoldingRequest(ticker, quantity, price)).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("TOOLONG", 10, 100, "Ticker")]
    [InlineData("", 10, 100, "Ticker")]
    [InlineData("BRK.B", 10, 100, "Ticker")]
    [InlineData("'; DROP TABLE portfolio.holdings; --", 10, 100, "Ticker")]
    [InlineData("AAPL", 0, 100, "Quantity")]
    [InlineData("AAPL", -1, 100, "Quantity")]
    [InlineData("AAPL", 0.0000001, 100, "Quantity")]
    [InlineData("AAPL", 10, 0, "Price")]
    [InlineData("AAPL", 10, -5, "Price")]
    public void Add_RejectsAndNamesTheField(string ticker, decimal quantity, decimal price, string field) =>
        Add.Validate(new AddHoldingRequest(ticker, quantity, price))
            .Errors.Select(error => error.PropertyName)
            .ShouldContain(field);

    // numeric(18,6) holds twelve integer digits, so 1e12 is the first value the INSERT would refuse
    // with 22003. Caught here it is a 400 naming the field; missed here it is a bare 500.
    // Decimal literals rather than InlineData: 999999999999.999999 has eighteen significant digits and
    // xUnit's double-to-decimal conversion keeps fifteen, which would silently test 1e12 instead.
    [Fact]
    public void Add_AcceptsTheLargestValueTheColumnHolds() =>
        Add.Validate(new AddHoldingRequest(
                "AAPL",
                AddHoldingRequestValidator.MaximumStorableValue,
                AddHoldingRequestValidator.MaximumStorableValue))
            .IsValid.ShouldBeTrue();

    [Theory]
    [InlineData(1e12, 100, "Quantity")]
    [InlineData(10, 1e12, "Price")]
    public void Add_RejectsAValueTooLargeForTheColumn(decimal quantity, decimal price, string field) =>
        Add.Validate(new AddHoldingRequest("AAPL", quantity, price))
            .Errors.Select(error => error.PropertyName)
            .ShouldContain(field);

    [Theory]
    [InlineData(15, 120)]
    [InlineData(0.000001, 0.01)]
    public void Update_AcceptsAWellFormedCorrection(decimal quantity, decimal price) =>
        Update.Validate(new UpdateHoldingRequest(quantity, price)).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData(0, 100, "Quantity")]
    [InlineData(-1, 100, "Quantity")]
    [InlineData(0.0000001, 100, "Quantity")]
    [InlineData(10, 0, "Price")]
    [InlineData(10, -5, "Price")]
    public void Update_RejectsAndNamesTheField(decimal quantity, decimal price, string field) =>
        Update.Validate(new UpdateHoldingRequest(quantity, price))
            .Errors.Select(error => error.PropertyName)
            .ShouldContain(field);

    [Fact]
    public void Update_AcceptsTheLargestValueTheColumnHolds() =>
        Update.Validate(new UpdateHoldingRequest(
                AddHoldingRequestValidator.MaximumStorableValue,
                AddHoldingRequestValidator.MaximumStorableValue))
            .IsValid.ShouldBeTrue();

    [Theory]
    [InlineData(1e12, 100, "Quantity")]
    [InlineData(10, 1e12, "Price")]
    public void Update_RejectsAValueTooLargeForTheColumn(decimal quantity, decimal price, string field) =>
        Update.Validate(new UpdateHoldingRequest(quantity, price))
            .Errors.Select(error => error.PropertyName)
            .ShouldContain(field);

    // The request is the only thing that binds off the wire, and it carries no user field. If one is
    // ever added, a client could post holdings into someone else's portfolio.
    [Fact]
    public void AddHoldingRequest_CarriesNoUserField() =>
        typeof(AddHoldingRequest)
            .GetProperties()
            .Select(property => property.Name)
            .ShouldBe(["Ticker", "Quantity", "Price"], ignoreOrder: true);
}
