using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.Infrastructure.Persistence.Converters;

/// <summary>Maps the strongly-typed UserId to the plain Guid the database stores.</summary>
internal sealed class UserIdConverter : ValueConverter<UserId, Guid>
{
    public UserIdConverter()
        : base(id => id.Value, value => new UserId(value))
    {
    }
}
