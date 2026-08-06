using System.Net;

namespace StockPortfolio.Tests;

/// <summary>A stub transport that answers with one fixed status and counts how many times it was asked.
/// With <paramref name="thenOk"/>, every call after the first answers 200 with a minimal quote body,
/// so a caller can prove a retry happened rather than just that the first attempt failed.</summary>
internal sealed class CountingHandler(
    HttpStatusCode status,
    string body = "{}",
    string contentType = "application/json",
    bool thenOk = false) : HttpMessageHandler
{
    public int Calls { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls++;

        var isRetrySucceeding = thenOk && Calls > 1;

        return Task.FromResult(new HttpResponseMessage(isRetrySucceeding ? HttpStatusCode.OK : status)
        {
            Content = new StringContent(
                isRetrySucceeding ? """{"c":187.42,"t":1780000000}""" : body,
                System.Text.Encoding.UTF8,
                isRetrySucceeding ? "application/json" : contentType),
        });
    }
}
