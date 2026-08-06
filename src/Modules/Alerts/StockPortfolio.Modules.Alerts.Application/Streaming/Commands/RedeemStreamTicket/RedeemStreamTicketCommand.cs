namespace StockPortfolio.Modules.Alerts.Application.Streaming.Commands.RedeemStreamTicket;

/// <summary>Spends a ticket. This is the stream's authentication, so there is nothing else in it.</summary>
public sealed record RedeemStreamTicketCommand(string Ticket);
