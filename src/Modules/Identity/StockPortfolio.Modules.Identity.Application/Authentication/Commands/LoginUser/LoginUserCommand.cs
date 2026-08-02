namespace StockPortfolio.Modules.Identity.Application.Authentication.Commands.LoginUser;

/// <summary>Sign in with an existing account.</summary>
public sealed record LoginUserCommand(string Email, string Password);
