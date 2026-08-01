using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.Infrastructure.Persistence.Converters;

/// <summary>
/// Maps the strongly-typed <see cref="UserId"/> to the plain <see cref="Guid"/> the database stores.
/// </summary>
/// <remarks>
/// This lives in <c>.Infrastructure</c> and not next to <see cref="UserId"/> in <c>.Domain</c> on purpose:
/// <see cref="ValueConverter{TModel,TProvider}"/> is an EF Core type, and the domain project must never
/// reference EF Core. This converter is the seam that keeps it out.
/// </remarks>
internal sealed class UserIdConverter : ValueConverter<UserId, Guid>
{
    public UserIdConverter()
        : base(id => id.Value, value => new UserId(value))
    {
    }
}
