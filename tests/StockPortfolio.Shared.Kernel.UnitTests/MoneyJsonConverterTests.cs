using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shouldly;
using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Tests;

public sealed class MoneyJsonConverterTests
{
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web) { Converters = { new MoneyJsonConverter() } };

    private static readonly JsonSerializerOptions StrictOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.Strict,
        Converters = { new MoneyJsonConverter() },
    };

    [Fact]
    public void Write_EmitsAmountAsString_NotAsNumber() =>
        JsonSerializer.Serialize(Money.Usd(125.5m), Options)
            .ShouldBe("""{"amount":"125.5","currency":"USD"}""");

    // Parsed, not an InlineData decimal literal: the double xUnit would convert drops the trailing zeroes this test is about.
    [Theory]
    [InlineData("125.000000")]
    [InlineData("1.500000")]
    public void Write_PreservesTrailingZeroes_SoSixDecimalsSurvive(string amount) =>
        JsonSerializer.Serialize(Money.Usd(decimal.Parse(amount, CultureInfo.InvariantCulture)), Options)
            .ShouldBe($$"""{"amount":"{{amount}}","currency":"USD"}""");

    [Fact]
    public void RoundTrip_PreservesAmountAndCurrency()
    {
        var original = Money.Usd(1234.567891m);

        JsonSerializer.Deserialize<Money>(JsonSerializer.Serialize(original, Options), Options)
            .ShouldBe(original);
    }

    [Fact]
    public void Read_AcceptsTheStringForm_UnderStrictNumberHandling() =>
        JsonSerializer.Deserialize<Money>("""{"amount":"7.25","currency":"usd"}""", StrictOptions)
            .ShouldBe(Money.Usd(7.25m));

    [Fact]
    public void Read_MissingCurrency_Throws() =>
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Money>("""{"amount":"1"}""", Options));

    // Without a whole-subtree skip the converter binds "XXX" from inside `meta` and eats the outer EndObject.
    [Fact]
    public void Read_UnknownNestedProperty_IsSkippedWhole()
    {
        const string Json = """{"amount":"1","meta":{"currency":"XXX"},"currency":"USD"}""";

        JsonSerializer.Deserialize<Money>(Json, Options).ShouldBe(Money.Usd(1m));
    }
}
