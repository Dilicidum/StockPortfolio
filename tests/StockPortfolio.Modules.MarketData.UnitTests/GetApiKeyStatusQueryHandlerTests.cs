using Shouldly;

using StockPortfolio.Modules.MarketData.Application.Keys.Queries.GetApiKeyStatus;
using StockPortfolio.Modules.MarketData.Domain;
using StockPortfolio.Tests.Fakes;

namespace StockPortfolio.Tests;

public sealed class GetApiKeyStatusQueryHandlerTests
{
    private static readonly Guid AUser = Guid.CreateVersion7();

    [Fact]
    public async Task Handle_ForANewUser_IsNotConfigured()
    {
        var handler = new GetApiKeyStatusQueryHandler(new FakeUserProviderKeyRepository(), new FakeSecretProtector());

        var result = await handler.Handle(new GetApiKeyStatusQuery(AUser), TestContext.Current.CancellationToken);

        result.ShouldBe(new GetApiKeyStatusResult(false, null, false));
    }

    [Fact]
    public async Task Handle_WithAGoodKey_IsConfiguredAndNotRejected()
    {
        var protector = new FakeSecretProtector();
        var repository = new FakeUserProviderKeyRepository();
        repository.Saved.Add(UserProviderKey.Create(AUser, protector.Protect("a-real-key"), "a1b2", TimeProvider.System));

        var handler = new GetApiKeyStatusQueryHandler(repository, protector);

        var result = await handler.Handle(new GetApiKeyStatusQuery(AUser), TestContext.Current.CancellationToken);

        result.ShouldBe(new GetApiKeyStatusResult(true, "a1b2", false));
    }

    [Fact]
    public async Task Handle_WithAProviderRejectedKey_IsRejected()
    {
        var protector = new FakeSecretProtector();
        var repository = new FakeUserProviderKeyRepository();
        var key = UserProviderKey.Create(AUser, protector.Protect("a-real-key"), "a1b2", TimeProvider.System);
        key.MarkRejected(TimeProvider.System);
        repository.Saved.Add(key);

        var handler = new GetApiKeyStatusQueryHandler(repository, protector);

        var result = await handler.Handle(new GetApiKeyStatusQuery(AUser), TestContext.Current.CancellationToken);

        result.Rejected.ShouldBeTrue();
    }

    /// <summary>The status this test exists for: a key nobody could ever decrypt again must not read as
    /// healthy just because the provider never got a chance to reject it.</summary>
    [Fact]
    public async Task Handle_WithACiphertextThatCannotBeDecrypted_IsRejected()
    {
        var protector = new FakeSecretProtector();
        var repository = new FakeUserProviderKeyRepository();

        // Not run through protector.Protect: FakeSecretProtector.Unprotect returns null for anything
        // missing its "protected:" prefix, mimicking a rotated-away key ring or a tampered row.
        repository.Saved.Add(UserProviderKey.Create(AUser, "unreadable-ciphertext", "a1b2", TimeProvider.System));

        var handler = new GetApiKeyStatusQueryHandler(repository, protector);

        var result = await handler.Handle(new GetApiKeyStatusQuery(AUser), TestContext.Current.CancellationToken);

        result.ShouldBe(new GetApiKeyStatusResult(true, "a1b2", true));
    }
}
