using System.Runtime.CompilerServices;

// The unit tests exercise the DbContext, the repositories and the Redis stores, all internal here.
// Its own file: riding on another type's file means deleting that type silently deletes this.
[assembly: InternalsVisibleTo("StockPortfolio.Modules.Alerts.UnitTests")]
