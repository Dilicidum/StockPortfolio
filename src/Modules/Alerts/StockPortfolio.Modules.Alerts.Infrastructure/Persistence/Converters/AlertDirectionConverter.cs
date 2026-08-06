using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using StockPortfolio.Modules.Alerts.Domain;

namespace StockPortfolio.Modules.Alerts.Infrastructure.Persistence.Converters;

// Stored as a name rather than the default int, which would tie every stored row to the order the
// enum members happen to be declared in. An enum is a custom mapped type like any other value object.

/// <summary>Maps AlertDirection to the text the database stores — "Fall" or "Rise".</summary>
internal sealed class AlertDirectionConverter : EnumToStringConverter<AlertDirection>;
