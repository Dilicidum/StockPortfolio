using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StockPortfolio.Shared.Kernel;

public sealed class MoneyJsonConverter : JsonConverter<Money>
{
    private const string AmountName = "amount";
    private const string CurrencyName = "currency";

    public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Money must be an object with 'amount' and 'currency'.");
        }

        decimal? amount = null;
        string? currency = null;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            var name = reader.GetString();
            reader.Read();

            if (string.Equals(name, AmountName, StringComparison.Ordinal))
            {
                // The string form is the point: a quoted number is what NumberHandling.Strict would otherwise reject.
                amount = reader.TokenType == JsonTokenType.String
                    ? decimal.Parse(reader.GetString()!, CultureInfo.InvariantCulture)
                    : reader.GetDecimal();
            }
            else if (string.Equals(name, CurrencyName, StringComparison.Ordinal))
            {
                currency = reader.GetString();
            }
            else
            {
                // Skip the whole value: stepping in would consume this object's EndObject and hand back a half-read reader.
                reader.Skip();
            }
        }

        if (amount is null || string.IsNullOrWhiteSpace(currency))
        {
            throw new JsonException("Money requires both 'amount' and 'currency'.");
        }

        return new Money(amount.Value, currency);
    }

    public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        // Written literally: a converter emits raw names, so the global camelCase policy never sees these.
        writer.WriteStartObject();
        writer.WriteString(AmountName, value.Amount.ToString(CultureInfo.InvariantCulture));
        writer.WriteString(CurrencyName, value.Currency);
        writer.WriteEndObject();
    }
}
