using OneOf;
using StockPortfolio.Modules.Identity.Domain;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Identity.Application.Authentication.Commands.RegisterUser;

/// <summary>
/// Every way registration can end: signed in, the address is taken, or the address is not an
/// address.
/// </summary>
/// <remarks>
/// Exhaustiveness is structural. <c>.Match</c> takes one delegate per case, so adding a fourth case
/// breaks every call site at compile time. Never <c>switch</c> over <c>.Value</c> — that is the one
/// way to lose the guarantee.
/// </remarks>
[GenerateOneOf]
public partial class RegisterUserResult : OneOfBase<TokenPair, EmailAlreadyUsed, ValidationFailed>
{
}
