namespace StockPortfolio.Api.IntegrationTests.Infrastructure;

/// <summary>
/// The one collection every integration test belongs to.
/// </summary>
/// <remarks>
/// Membership of a single collection does two things at once: it shares one
/// <see cref="ApiFixture"/> — and therefore one Postgres container, one Redis container and one API
/// host — across the whole assembly, and it serialises the tests, so nothing races on the shared
/// database. Tests still give themselves unique email addresses: sequential is not the same as
/// ordered, and no test may depend on another having run first.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class ApiCollectionDefinition : ICollectionFixture<ApiFixture>
{
    /// <summary>The collection name. Put it on every integration test class.</summary>
    public const string Name = "api";
}
