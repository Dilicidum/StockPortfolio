using System.Net;

namespace StockPortfolio.Tests;

/// <summary>A stub transport that answers with one fixed status and counts how many times it was asked.</summary>
internal sealed class CountingHandler(HttpStatusCode status, string body = "{}") : HttpMessageHandler
{
    public int Calls { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls++;

        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        });
    }
}
