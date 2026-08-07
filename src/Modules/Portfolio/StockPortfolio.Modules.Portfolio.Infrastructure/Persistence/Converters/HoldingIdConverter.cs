using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using StockPortfolio.Modules.Portfolio.Domain;

namespace StockPortfolio.Modules.Portfolio.Infrastructure.Persistence.Converters;

internal sealed class HoldingIdConverter : ValueConverter<HoldingId, Guid>
{
    public HoldingIdConverter()
        : base(id => id.Value, value => new HoldingId(value))
    {
    }
}
