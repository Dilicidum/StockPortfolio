namespace StockPortfolio.Modules.Alerts.Application.Streaming.Commands.IssueStreamTicket;

/// <summary>Asks for a ticket. The bearer token on the request is the whole of the input.</summary>
public sealed record IssueStreamTicketCommand(Guid UserId);
