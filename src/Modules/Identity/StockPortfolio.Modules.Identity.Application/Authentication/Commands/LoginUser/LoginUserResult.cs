using OneOf;
using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.Application.Authentication.Commands.LoginUser;

/// <summary>Every way signing in can end: signed in, or not.</summary>
[GenerateOneOf]
public partial class LoginUserResult : OneOfBase<TokenPair, InvalidCredentials>
{
}
