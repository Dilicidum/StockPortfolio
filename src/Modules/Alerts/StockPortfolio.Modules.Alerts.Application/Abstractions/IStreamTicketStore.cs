namespace StockPortfolio.Modules.Alerts.Application.Abstractions;

/// <summary>The short-lived, single-use credential a browser can put in a URL when it cannot set a header.</summary>
public interface IStreamTicketStore
{
    /// <summary>Stores the ticket against the user for its whole lifetime.</summary>
    Task IssueAsync(string ticket, Guid userId, TimeSpan lifetime, CancellationToken ct);

    /// <summary>Reads and deletes in one operation, so two connections cannot redeem the same ticket.</summary>
    Task<Guid?> RedeemAsync(string ticket, CancellationToken ct);
}
