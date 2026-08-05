using System.Runtime.CompilerServices;

// The unit tests exercise the provider, the response mapping and the Redis store, all internal here.
// Its own file: riding on another type's file means deleting that type silently deletes this.
[assembly: InternalsVisibleTo("StockPortfolio.Modules.MarketData.UnitTests")]
