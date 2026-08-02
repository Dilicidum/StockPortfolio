using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.Infrastructure.Persistence.Converters;

/// <summary>Maps the strongly-typed RefreshTokenId to the plain Guid the database stores.</summary>
internal sealed class RefreshTokenIdConverter : ValueConverter<RefreshTokenId, Guid>
{
    public RefreshTokenIdConverter()
        : base(id => id.Value, value => new RefreshTokenId(value))
    {
    }
}
