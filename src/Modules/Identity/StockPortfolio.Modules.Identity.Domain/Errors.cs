namespace StockPortfolio.Modules.Identity.Domain;

// Only genuine failure cases live here.
//
// `Success` and `NotFound` used to be declared in this file. Both were mistakes: OneOf already
// ships `OneOf.Types.Success` and `OneOf.Types.NotFound`, so redeclaring them bought nothing and
// gave every module its own incompatible spelling of the same idea — and `Success` is not an error,
// so a file named Errors.cs was the wrong home for it regardless. Use the OneOf.Types versions.

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
