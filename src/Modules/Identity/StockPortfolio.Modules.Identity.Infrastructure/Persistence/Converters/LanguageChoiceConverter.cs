using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.Infrastructure.Persistence.Converters;

// Stored as a name, not the default int, so stored rows do not depend on enum declaration order.
internal sealed class LanguageChoiceConverter : EnumToStringConverter<LanguageChoice>;
