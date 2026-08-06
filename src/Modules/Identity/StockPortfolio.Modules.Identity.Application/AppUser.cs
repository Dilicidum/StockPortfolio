using Microsoft.AspNetCore.Identity;

namespace StockPortfolio.Modules.Identity.Application;

/// <summary>The account, as ASP.NET Core Identity stores it. Keyed on Guid, not the default string.</summary>
/// <remarks>
/// It lives in .Application rather than .Domain for a reason the layering forces: .Api needs it to inject
/// UserManager&lt;AppUser&gt;, .Infrastructure needs it for the EF store, and those two may not reference each
/// other — Application is the only layer both see. It would also fail rule 3 in .Domain, because
/// IdentityUser exposes public setters on every property and that rule forbids them.
///
/// Guid, because Portfolio, MarketData and Alerts all store the owner as a uuid. Leaving it as the default
/// string worked only because the default id happens to be a Guid in disguise, and it forced this module's
/// own user_preferences key to be text while every sibling table used uuid.
/// </remarks>
public sealed class AppUser : IdentityUser<Guid>;
