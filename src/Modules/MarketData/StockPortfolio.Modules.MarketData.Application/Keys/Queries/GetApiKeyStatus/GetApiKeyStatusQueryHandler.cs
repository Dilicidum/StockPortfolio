using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.MarketData.Application.Keys.Queries.GetApiKeyStatus;

/// <summary>Reads the caller's key status. No row is "not configured", never an error.</summary>
public sealed class GetApiKeyStatusQueryHandler(IUserProviderKeyRepository repository)
    : IQueryHandler<GetApiKeyStatusQuery, GetApiKeyStatusResult>
{
    /// <inheritdoc/>
    public async Task<GetApiKeyStatusResult> Handle(GetApiKeyStatusQuery query, CancellationToken ct)
    {
        var key = await repository.FindAsync(query.UserId, ct);

        return key is null
            ? new GetApiKeyStatusResult(false, null, false)
            : new GetApiKeyStatusResult(true, key.LastFour, key.LastRejectedAt is not null);
    }
}
