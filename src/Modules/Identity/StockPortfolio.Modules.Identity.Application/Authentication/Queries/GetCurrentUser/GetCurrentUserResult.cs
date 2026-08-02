using OneOf;
using StockPortfolio.Modules.Identity.Domain;
using OneOf.Types;

namespace StockPortfolio.Modules.Identity.Application.Authentication.Queries.GetCurrentUser;

/// <summary>
/// Every way reading the current user can end: the summary, or a token whose account has gone.
/// </summary>
/// <remarks>
/// The second case is not dead. An access token stays valid for its whole lifetime, so a deleted
/// account keeps presenting a perfectly well-signed token until it expires.
/// </remarks>
[GenerateOneOf]
public partial class GetCurrentUserResult : OneOfBase<UserSummary, NotFound>
{
}
