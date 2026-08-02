using OneOf;
using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.Application.Authentication.Commands.LoginUser;

/// <summary>
/// Every way signing in can end: signed in, or not.
/// </summary>
/// <remarks>
/// <b>Two cases, not three.</b> There is deliberately no separate "no such account" case. Splitting
/// them would let anyone with a list of addresses learn which ones have accounts here, and no
/// amount of care in the wording of the 401 would close that. The handler also spends the same
/// time on both paths — see <see cref="Abstractions.IPasswordHasher.DummyHash"/> — because a
/// response that comes back faster leaks the same fact.
/// </remarks>
[GenerateOneOf]
public partial class LoginUserResult : OneOfBase<TokenPair, InvalidCredentials>
{
}
