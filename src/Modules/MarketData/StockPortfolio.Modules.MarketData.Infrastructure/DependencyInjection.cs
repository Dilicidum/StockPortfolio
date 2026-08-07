using Microsoft.Extensions.DependencyInjection;

using OneOf;
using OneOf.Types;

using StockPortfolio.Modules.MarketData.Application.Keys.Commands.RemoveApiKey;
using StockPortfolio.Modules.MarketData.Application.Keys.Commands.SaveApiKey;
using StockPortfolio.Modules.MarketData.Application.Keys.Queries.GetApiKeyStatus;
using StockPortfolio.Modules.MarketData.Application.Tickers.Queries.SearchTickers;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.MarketData.Infrastructure;

internal static class DependencyInjection
{
    internal static IServiceCollection AddMarketDataHandlers(this IServiceCollection services)
    {
        services.AddScoped<
            IQueryHandler<SearchTickersQuery, IReadOnlyList<SearchTickersResult>>,
            SearchTickersQueryHandler>();

        services.AddScoped<
            ICommandHandler<
                SaveApiKeyCommand,
                OneOf<SaveApiKeyResult, ProviderRejectedTheKey, ProviderCouldNotAnswer, ByokDisabled>>,
            SaveApiKeyCommandHandler>();

        services.AddScoped<
            ICommandHandler<RemoveApiKeyCommand, OneOf<Success, NotFound>>,
            RemoveApiKeyCommandHandler>();

        services.AddScoped<
            IQueryHandler<GetApiKeyStatusQuery, GetApiKeyStatusResult>,
            GetApiKeyStatusQueryHandler>();

        return services;
    }
}
