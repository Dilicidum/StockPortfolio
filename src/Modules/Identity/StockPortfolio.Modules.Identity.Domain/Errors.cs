namespace StockPortfolio.Modules.Identity.Domain;

// Only genuine failure cases live here.

/// <summary>Registration was refused because the address is already in use.</summary>
public sealed record EmailAlreadyUsed;

/// <summary>Sign-in was refused.</summary>
public sealed record InvalidCredentials;

/// <summary>The presented refresh token is unknown, expired, or has already been rotated or revoked.</summary>
public sealed record InvalidOrExpired;
