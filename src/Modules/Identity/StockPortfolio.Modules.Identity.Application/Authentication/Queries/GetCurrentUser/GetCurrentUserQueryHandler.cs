using StockPortfolio.Modules.Identity.Application.Abstractions;
using StockPortfolio.Modules.Identity.Domain;
using StockPortfolio.Shared.Kernel.Cqrs;
using OneOf.Types;

namespace StockPortfolio.Modules.Identity.Application.Authentication.Queries.GetCurrentUser;

/// <summary>Reads the signed-in user.</summary>
public sealed class GetCurrentUserQueryHandler(IUserRepository users)
    : IQueryHandler<GetCurrentUserQuery, GetCurrentUserResult>
{
    /// <inheritdoc/>
    public async Task<GetCurrentUserResult> Handle(GetCurrentUserQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var user = await users.FindByIdAsync(new UserId(query.UserId), ct).ConfigureAwait(false);

        if (user is null)
        {
            return new NotFound();
        }

        return new UserSummary(user.Id.Value, user.Email);
    }
}
