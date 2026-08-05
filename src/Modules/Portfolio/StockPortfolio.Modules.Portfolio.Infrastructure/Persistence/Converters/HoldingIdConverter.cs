using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using StockPortfolio.Modules.Portfolio.Domain;

namespace StockPortfolio.Modules.Portfolio.Infrastructure.Persistence.Converters;

/// <summary>Maps the strongly-typed HoldingId to the plain Guid the database stores.</summary>
internal sealed class HoldingIdConverter : ValueConverter<HoldingId, Guid>
{
    public HoldingIdConverter()
        : base(id => id.Value, value => new HoldingId(value))
    {
    }
}
