using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.MarketData.Application.Keys.Queries.GetApiKeyStatus;

public sealed class GetApiKeyStatusQueryHandler(IUserProviderKeyRepository repository, ISecretProtector protector)
    : IQueryHandler<GetApiKeyStatusQuery, GetApiKeyStatusResult>
{
    public async Task<GetApiKeyStatusResult> Handle(GetApiKeyStatusQuery query, CancellationToken ct)
    {
        var key = await repository.FindAsync(query.UserId, ct);

        if (key is null)
        {
            return new GetApiKeyStatusResult(false, null, false);
        }

        var undecryptable = protector.Unprotect(key.Ciphertext) is null;

        return new GetApiKeyStatusResult(true, key.LastFour, key.LastRejectedAt is not null || undecryptable);
    }
}
