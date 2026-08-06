namespace StockPortfolio.Modules.Alerts.Application.Streaming.Commands.IssueStreamTicket;

/// <summary>The ticket and when it stops working, so the client can decide not to bother.</summary>
public sealed record IssueStreamTicketResult(string Ticket, DateTimeOffset ExpiresAt);
