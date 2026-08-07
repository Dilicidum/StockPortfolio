using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.Infrastructure.Persistence.Converters;

internal sealed class ThemeChoiceConverter : EnumToStringConverter<ThemeChoice>;
