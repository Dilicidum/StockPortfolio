using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.Infrastructure.Persistence.Converters;

// Stored as a name rather than the default int, which would tie every stored row to the order the
// enum members happen to be declared in. An enum is a custom mapped type like any other value object.
internal sealed class ThemeChoiceConverter : EnumToStringConverter<ThemeChoice>;
