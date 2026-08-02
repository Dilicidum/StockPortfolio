using OneOf;
using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.Application.Authentication.Commands.RefreshSession;

/// <summary>Every way a refresh can end: a new pair, or a token that is unknown, expired, already rotated or.</summary>
[GenerateOneOf]
public partial class RefreshSessionResult : OneOfBase<TokenPair, InvalidOrExpired>
{
}
