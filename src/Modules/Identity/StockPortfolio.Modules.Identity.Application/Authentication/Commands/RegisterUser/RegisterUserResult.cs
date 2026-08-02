using OneOf;
using StockPortfolio.Modules.Identity.Domain;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Identity.Application.Authentication.Commands.RegisterUser;

/// <summary>Every way registration can end: signed in, the address is taken, or the address is not an address.</summary>
[GenerateOneOf]
public partial class RegisterUserResult : OneOfBase<TokenPair, EmailAlreadyUsed, ValidationFailed>
{
}
