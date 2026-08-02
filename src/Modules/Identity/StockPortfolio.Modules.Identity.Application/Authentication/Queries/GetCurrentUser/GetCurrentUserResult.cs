using OneOf;
using StockPortfolio.Modules.Identity.Domain;
using OneOf.Types;

namespace StockPortfolio.Modules.Identity.Application.Authentication.Queries.GetCurrentUser;

/// <summary>Every way reading the current user can end: the summary, or a token whose account has gone.</summary>
[GenerateOneOf]
public partial class GetCurrentUserResult : OneOfBase<UserSummary, NotFound>
{
}
