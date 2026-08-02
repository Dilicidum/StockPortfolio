using OneOf;
using OneOf.Types;
using StockPortfolio.Modules.Identity.Application.Abstractions;
using StockPortfolio.Modules.Identity.Domain;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Identity.Application.Authentication.Queries.GetCurrentUser;

/// <summary>Reads the signed-in user.</summary>
public sealed class GetCurrentUserQueryHandler(IUserRepository users)
    : IQueryHandler<GetCurrentUserQuery, OneOf<GetCurrentUserResult, NotFound>>
{
    /// <inheritdoc/>
    public async Task<OneOf<GetCurrentUserResult, NotFound>> Handle(GetCurrentUserQuery query, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(new UserId(query.UserId), ct);

        if (user is null)
        {
            return new NotFound();
        }

        return new GetCurrentUserResult(user.Id.Value, user.Email);
    }
}
