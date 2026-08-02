using System.Runtime.CompilerServices;

// The unit tests exercise Argon2PasswordHasher and PhcString directly.
// Its own file: riding on another type's file means deleting that type silently deletes this.
[assembly: InternalsVisibleTo("StockPortfolio.Modules.Identity.UnitTests")]
