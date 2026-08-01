using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.Infrastructure.Persistence.Converters;

/// <summary>
/// Maps the strongly-typed <see cref="RefreshTokenId"/> to the plain <see cref="Guid"/> the database stores.
/// </summary>
/// <remarks>
/// Same reasoning as <see cref="UserIdConverter"/>: EF Core types stay out of <c>.Domain</c>.
/// </remarks>
internal sealed class RefreshTokenIdConverter : ValueConverter<RefreshTokenId, Guid>
{
    public RefreshTokenIdConverter()
        : base(id => id.Value, value => new RefreshTokenId(value))
    {
    }
}
