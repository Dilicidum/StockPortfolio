using OneOf;
using StockPortfolio.Modules.Identity.Domain;
using OneOf.Types;

namespace StockPortfolio.Modules.Identity.Application.Revoke;

/// <summary>
/// Every way a logout can end: the session was closed, or there was no live session to close.
/// </summary>
[GenerateOneOf]
public partial class RevokeResult : OneOfBase<Success, NotFound>
{
}
