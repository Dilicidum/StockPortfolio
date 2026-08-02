using OneOf;
using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.Application.Authentication.Commands.RefreshSession;

/// <summary>
/// Every way a refresh can end: a new pair, or a token that is unknown, expired, already rotated
/// or revoked.
/// </summary>
/// <remarks>
/// One failure case, for the same reason <see cref="LoginUser.LoginUserResult"/> has one: telling a caller
/// <i>why</i> their token was rejected tells an attacker holding a stolen token what to try next.
/// </remarks>
[GenerateOneOf]
public partial class RefreshSessionResult : OneOfBase<TokenPair, InvalidOrExpired>
{
}
