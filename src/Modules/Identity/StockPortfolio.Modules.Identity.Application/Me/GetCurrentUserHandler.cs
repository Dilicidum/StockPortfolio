using StockPortfolio.Modules.Identity.Application.Abstractions;
using StockPortfolio.Modules.Identity.Domain;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Identity.Application.Me;

/// <summary>
/// Reads the signed-in user. Changes nothing, so it is a query and takes no unit of work.
/// </summary>
/// <param name="users">Finds the account.</param>
public sealed class GetCurrentUserHandler(IUserRepository users)
    : IQueryHandler<GetCurrentUser, CurrentUserResult>
{
    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
    public async Task<CurrentUserResult> Handle(GetCurrentUser query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var user = await users.FindByIdAsync(new UserId(query.UserId), ct).ConfigureAwait(false);

        if (user is null)
        {
            return new SessionNotFound();
        }

        return new UserSummary(user.Id.Value, user.Email);
    }
}
