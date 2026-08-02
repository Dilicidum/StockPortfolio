namespace StockPortfolio.Api.IntegrationTests.Infrastructure;

/// <summary>The one collection every integration test belongs to.</summary>
[CollectionDefinition(Name)]
public sealed class ApiCollectionDefinition : ICollectionFixture<ApiFixture>
{
    /// <summary>The collection name.</summary>
    public const string Name = "api";
}
