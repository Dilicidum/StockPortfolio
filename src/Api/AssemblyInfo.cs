using System.Runtime.CompilerServices;

// The startup checks run before any request and are internal to the host, so the only way to drive
// them is from inside. Its own file: riding on another type's file means deleting that type silently
// deletes this.
[assembly: InternalsVisibleTo("StockPortfolio.Api.IntegrationTests")]
