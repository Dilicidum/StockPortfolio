using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.MarketData.Application.Keys.Queries.GetApiKeyStatus;

/// <summary>Reads the caller's key status. No row is "not configured", never an error.</summary>
public sealed class GetApiKeyStatusQueryHandler(IUserProviderKeyRepository repository, ISecretProtector protector)
    : IQueryHandler<GetApiKeyStatusQuery, GetApiKeyStatusResult>
{
    /// <inheritdoc/>
    public async Task<GetApiKeyStatusResult> Handle(GetApiKeyStatusQuery query, CancellationToken ct)
    {
        var key = await repository.FindAsync(query.UserId, ct);

        if (key is null)
        {
            return new GetApiKeyStatusResult(false, null, false);
        }

        // A key that cannot be decrypted must surface the same way a provider-rejected one does: it is on
        // file, but it is not usable, and a bare "Configured: true, Rejected: false" would read as healthy.
        var undecryptable = protector.Unprotect(key.Ciphertext) is null;

        return new GetApiKeyStatusResult(true, key.LastFour, key.LastRejectedAt is not null || undecryptable);
    }
}
