using OneOf;

using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Domain;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.MarketData.Application.Keys.Commands.SaveApiKey;

/// <summary>Validates a candidate key against the live provider, then stores it encrypted. Never the plaintext.</summary>
public sealed class SaveApiKeyCommandHandler(
    IUserProviderKeyRepository repository,
    IQuoteProvider provider,
    ISecretProtector protector,
    ByokOptions options,
    TimeProvider clock)
    : ICommandHandler<
        SaveApiKeyCommand,
        OneOf<SaveApiKeyResult, ProviderRejectedTheKey, ProviderCouldNotAnswer, ByokDisabled>>
{
    /// <inheritdoc/>
    public async Task<OneOf<SaveApiKeyResult, ProviderRejectedTheKey, ProviderCouldNotAnswer, ByokDisabled>> Handle(
        SaveApiKeyCommand command,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!options.Enabled)
        {
            return new ByokDisabled();
        }

        // Checked with the CANDIDATE key, never the app's own — VerifyKeyAsync is the one method on
        // IQuoteProvider that does not fail open, because an unanswerable check must not be read as valid.
        var verdict = await provider.VerifyKeyAsync(command.ApiKey, ct);

        if (verdict == KeyVerdict.Rejected)
        {
            return new ProviderRejectedTheKey();
        }

        if (verdict == KeyVerdict.Unknown)
        {
            return new ProviderCouldNotAnswer();
        }

        var ciphertext = protector.Protect(command.ApiKey);
        var lastFour = command.ApiKey.Length <= 4 ? command.ApiKey : command.ApiKey[^4..];

        var existing = await repository.FindAsync(command.UserId, ct);

        if (existing is not null)
        {
            existing.Replace(ciphertext, lastFour, clock);
            await repository.SaveAsync(existing, ct);

            return new SaveApiKeyResult(lastFour);
        }

        var created = UserProviderKey.Create(command.UserId, ciphertext, lastFour, clock);
        await repository.SaveAsync(created, ct);

        return new SaveApiKeyResult(lastFour);
    }
}
