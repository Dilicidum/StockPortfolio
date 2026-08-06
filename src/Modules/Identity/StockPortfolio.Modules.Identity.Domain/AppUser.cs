using Microsoft.AspNetCore.Identity;

namespace StockPortfolio.Modules.Identity.Domain;

/// <summary>The account, as ASP.NET Core Identity stores it. Keyed on Guid, not the default string.</summary>
/// <remarks>
/// Guid, because Portfolio, MarketData and Alerts all store the owner as a uuid. The default string key
/// worked only because the id it generates happens to be a Guid in disguise, and it forced this module's
/// own user_preferences key to be text while every sibling table used uuid.
///
/// Rule 3 polices it like any other domain type and passes, with no exemption: the rule inspects
/// DECLARED properties, and this type declares none — every property, public setter and all, is
/// inherited from IdentityUser. Give it a property of its own with a public setter and rule 3 fails,
/// which is the right answer. DomainShapeTests pins both halves of that.
/// </remarks>
public sealed class AppUser : IdentityUser<Guid>;
