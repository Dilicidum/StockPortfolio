namespace StockPortfolio.Modules.MarketData.Application.Abstractions;

/// <summary>Reads a user's own provider key back in plaintext, to fetch quotes on their behalf.</summary>
public interface IUserProviderKeyReader
{
    Task<string?> ReadPlaintextAsync(Guid userId, CancellationToken ct);
}
