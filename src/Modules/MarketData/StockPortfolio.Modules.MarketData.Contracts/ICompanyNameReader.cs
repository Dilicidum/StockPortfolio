namespace StockPortfolio.Modules.MarketData.Contracts;

/// <summary>Company names, kept apart from price and existence because this one is allowed to know nothing.</summary>
public interface ICompanyNameReader
{
    /// <summary>Reads whatever names are already cached. It never calls the provider, so a page using it can
    /// neither wait on nor fail because of one. A ticker with no known name is absent from the result, and
    /// that is the ordinary case for a position added before names existed. Keys are canonical upper case, Ordinal.</summary>
    Task<IReadOnlyDictionary<string, string>> GetNamesAsync(
        IReadOnlyCollection<string> tickers,
        CancellationToken ct);
}
