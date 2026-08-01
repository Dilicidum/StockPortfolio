namespace StockPortfolio.Modules.Identity.Domain;

/// <summary>
/// Registration was refused because the address is already in use.
/// </summary>
/// <remarks>
/// Decided by the unique index, not by a preceding <c>SELECT</c> — check-then-insert is a race.
/// </remarks>
public sealed record EmailAlreadyUsed;

/// <summary>
/// Sign-in was refused. Deliberately undifferentiated: there is no separate "no such account"
/// case, because telling the two apart would turn the login endpoint into an account-enumeration
/// oracle.
/// </summary>
public sealed record InvalidCredentials;

/// <summary>
/// The presented refresh token is unknown, expired, or has already been rotated or revoked.
/// One case for the same reason <see cref="InvalidCredentials"/> is one case.
/// </summary>
public sealed record InvalidOrExpired;

/// <summary>
/// The session or the user behind it no longer exists — a valid-looking token whose row has gone.
/// </summary>
public sealed record SessionNotFound;

/// <summary>
/// The operation completed and has nothing to report. Used where the alternative would be
/// <c>OneOf&lt;Unit, …&gt;</c>.
/// </summary>
public sealed record Success;
