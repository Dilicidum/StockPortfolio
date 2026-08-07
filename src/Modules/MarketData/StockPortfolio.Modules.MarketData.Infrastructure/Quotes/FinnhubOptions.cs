using Microsoft.Extensions.Configuration;

namespace StockPortfolio.Modules.MarketData.Infrastructure.Quotes;

internal sealed class FinnhubOptions
{
    public const string SectionName = "Finnhub";

    private const string DefaultBaseUrl = "https://api.finnhub.io/api/v1/";

    private FinnhubOptions(string apiKey, Uri baseUrl)
    {
        ApiKey = apiKey;
        BaseUrl = baseUrl;
    }

    public string ApiKey { get; }

    public Uri BaseUrl { get; }

    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

    public static FinnhubOptions FromConfiguration(IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var section = config.GetSection(SectionName);

        var baseUrl = Uri.TryCreate(section["BaseUrl"], UriKind.Absolute, out var parsed)
            ? parsed
            : new Uri(DefaultBaseUrl);

        return new FinnhubOptions(section["ApiKey"] ?? string.Empty, WithTrailingSlash(baseUrl));
    }

    private static Uri WithTrailingSlash(Uri baseUrl) =>
        baseUrl.AbsoluteUri.EndsWith('/') ? baseUrl : new Uri(baseUrl.AbsoluteUri + "/");
}
