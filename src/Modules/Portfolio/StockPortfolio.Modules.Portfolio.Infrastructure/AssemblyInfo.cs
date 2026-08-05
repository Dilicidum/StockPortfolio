using System.Runtime.CompilerServices;

// The unit tests build the EF model directly, which lives behind internal in this assembly.
// Its own file: riding on another type's file means deleting that type silently deletes this.
[assembly: InternalsVisibleTo("StockPortfolio.Modules.Portfolio.UnitTests")]
