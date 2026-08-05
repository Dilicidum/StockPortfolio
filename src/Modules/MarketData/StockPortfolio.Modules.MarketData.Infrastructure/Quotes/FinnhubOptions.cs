using Microsoft.Extensions.Configuration;

namespace StockPortfolio.Modules.MarketData.Infrastructure.Quotes;

/// <summary>The Finnhub endpoint and key. A missing key is a supported state, so nothing here throws.</summary>
internal sealed class FinnhubOptions
{
    /// <summary>The configuration section these values are read from.</summary>
    public const string SectionName = "Finnhub";

    // api.finnhub.io, not the marketing host in their OpenAPI spec, which sits behind a WAF.
    private const string DefaultBaseUrl = "https://api.finnhub.io/api/v1/";

    private FinnhubOptions(string apiKey, Uri baseUrl)
    {
        ApiKey = apiKey;
        BaseUrl = baseUrl;
    }

    /// <summary>The token sent as X-Finnhub-Token; empty means the fake provider is used instead.</summary>
    public string ApiKey { get; }

    /// <summary>Always ends in a slash, so relative request paths resolve under /api/v1/.</summary>
    public Uri BaseUrl { get; }

    /// <summary>Whether a real key is configured. The one question the module asks of these options.</summary>
    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>Reads the section. Must never throw — a throw here takes down `docker compose up`.</summary>
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
