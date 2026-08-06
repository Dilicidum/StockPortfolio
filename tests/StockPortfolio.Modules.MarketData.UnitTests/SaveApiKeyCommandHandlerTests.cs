using Shouldly;

using StockPortfolio.Modules.MarketData.Application;
using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Application.Keys.Commands.SaveApiKey;
using StockPortfolio.Modules.MarketData.Domain;
using StockPortfolio.Tests.Fakes;

namespace StockPortfolio.Tests;

public sealed class SaveApiKeyCommandHandlerTests
{
    private static readonly Guid AUser = Guid.CreateVersion7();

    private static IQuoteProvider AcceptsEveryKey => new StubProvider(KeyVerdict.Accepted);

    private static IQuoteProvider RejectsEveryKey => new StubProvider(KeyVerdict.Rejected);

    private static IQuoteProvider CannotAnswer => new StubProvider(KeyVerdict.Unknown);

    [Fact]
    public async Task Handle_WhenTheProviderRejectsTheKey_DoesNotStoreAnything()
    {
        var repository = new FakeUserProviderKeyRepository();
        var handler = AHandler(repository, provider: RejectsEveryKey);

        var result = await handler.Handle(
            new SaveApiKeyCommand(AUser, "bad-key"), TestContext.Current.CancellationToken);

        result.IsT1.ShouldBeTrue();
        repository.Saved.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WithAGoodKey_StoresCiphertextAndNeverThePlaintext()
    {
        var repository = new FakeUserProviderKeyRepository();
        var handler = AHandler(repository, provider: AcceptsEveryKey);

        await handler.Handle(
            new SaveApiKeyCommand(AUser, "d1v3rs3-k3y-a1b2"), TestContext.Current.CancellationToken);

        var stored = repository.Saved.ShouldHaveSingleItem();
        stored.Ciphertext.ShouldNotContain("d1v3rs3-k3y-a1b2");
        stored.LastFour.ShouldBe("a1b2");
    }

    [Fact]
    public async Task Handle_WhenTheProviderCannotAnswer_ReturnsItsOwnCase_AndStoresNothing()
    {
        var repository = new FakeUserProviderKeyRepository();
        var handler = AHandler(repository, provider: CannotAnswer);

        var result = await handler.Handle(
            new SaveApiKeyCommand(AUser, "a-key-nobody-could-check"), TestContext.Current.CancellationToken);

        // Distinct from Rejected: an unanswerable check must never be read as "your key is bad".
        result.IsT2.ShouldBeTrue();
        repository.Saved.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WhenByokIsDisabled_ReturnsItsOwnCase_WithoutAskingTheProvider()
    {
        var repository = new FakeUserProviderKeyRepository();
        var provider = new StubProvider(KeyVerdict.Accepted);
        var handler = AHandler(repository, provider, byokEnabled: false);

        var result = await handler.Handle(
            new SaveApiKeyCommand(AUser, "d1v3rs3-k3y-a1b2"), TestContext.Current.CancellationToken);

        result.IsT3.ShouldBeTrue();
        provider.VerifyCalls.ShouldBe(0, "a switched-off feature must not even ask the provider");
        repository.Saved.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_ReplacingAnExistingKey_ClearsAnyEarlierRejection()
    {
        var repository = new FakeUserProviderKeyRepository();
        var clock = TimeProvider.System;
        var existing = UserProviderKey.Create(AUser, "old-cipher", "1111", clock);
        existing.MarkRejected(clock);
        repository.Saved.Add(existing);

        var handler = AHandler(repository, provider: AcceptsEveryKey);

        await handler.Handle(
            new SaveApiKeyCommand(AUser, "d1v3rs3-k3y-a1b2"), TestContext.Current.CancellationToken);

        var stored = repository.Saved.ShouldHaveSingleItem();
        stored.LastRejectedAt.ShouldBeNull();
        stored.LastFour.ShouldBe("a1b2");
    }

    private static SaveApiKeyCommandHandler AHandler(
        FakeUserProviderKeyRepository repository,
        IQuoteProvider provider,
        bool byokEnabled = true) =>
        new(repository, provider, new FakeSecretProtector(), new ByokOptions(byokEnabled), TimeProvider.System);

    private sealed class StubProvider(KeyVerdict verdict) : IQuoteProvider
    {
        public int VerifyCalls { get; private set; }

        public string Name => "Stub";

        public Task<IReadOnlyList<Quote>> GetQuotesAsync(IReadOnlySet<Ticker> tickers, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Quote>>([]);

        public Task<bool> SymbolExistsAsync(Ticker ticker, CancellationToken ct) => Task.FromResult(true);

        public Task<IReadOnlyList<SymbolMatch>> SearchSymbolsAsync(string query, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SymbolMatch>>([]);

        public Task<KeyVerdict> VerifyKeyAsync(string apiKey, CancellationToken ct)
        {
            VerifyCalls++;

            return Task.FromResult(verdict);
        }
    }
}
