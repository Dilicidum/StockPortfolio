using OneOf;
using OneOf.Types;

using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.MarketData.Application.Keys.Commands.RemoveApiKey;

public sealed class RemoveApiKeyCommandHandler(IUserProviderKeyRepository repository)
    : ICommandHandler<RemoveApiKeyCommand, OneOf<Success, NotFound>>
{
    public async Task<OneOf<Success, NotFound>> Handle(RemoveApiKeyCommand command, CancellationToken ct)
    {
        var key = await repository.FindAsync(command.UserId, ct);

        if (key is null)
        {
            return new NotFound();
        }

        await repository.RemoveAsync(key, ct);

        return new Success();
    }
}
