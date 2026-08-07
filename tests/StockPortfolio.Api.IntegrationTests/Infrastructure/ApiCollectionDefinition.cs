namespace StockPortfolio.Api.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class ApiCollectionDefinition : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
