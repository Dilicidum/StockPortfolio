namespace StockPortfolio.Modules.Identity.Application.Authentication.Commands.RegisterUser;

/// <summary>Create an account and sign in with it in one step.</summary>
public sealed record RegisterUserCommand(string Email, string Password);
