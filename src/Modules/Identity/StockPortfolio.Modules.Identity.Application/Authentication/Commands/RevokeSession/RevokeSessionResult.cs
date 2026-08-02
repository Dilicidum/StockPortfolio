using OneOf;
using StockPortfolio.Modules.Identity.Domain;
using OneOf.Types;

namespace StockPortfolio.Modules.Identity.Application.Authentication.Commands.RevokeSession;

/// <summary>
/// Every way a logout can end: the session was closed, or there was no live session to close.
/// </summary>
[GenerateOneOf]
public partial class RevokeSessionResult : OneOfBase<Success, NotFound>
{
}
