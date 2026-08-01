using OneOf;
using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.Application.Refresh;

/// <summary>
/// Every way a refresh can end: a new pair, or a token that is unknown, expired, already rotated
/// or revoked.
/// </summary>
/// <remarks>
/// One failure case, for the same reason <see cref="Login.LoginResult"/> has one: telling a caller
/// <i>why</i> their token was rejected tells an attacker holding a stolen token what to try next.
/// </remarks>
[GenerateOneOf]
public partial class RefreshResult : OneOfBase<TokenPair, InvalidOrExpired>
{
}
