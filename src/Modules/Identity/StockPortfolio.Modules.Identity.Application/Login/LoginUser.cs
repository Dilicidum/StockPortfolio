namespace StockPortfolio.Modules.Identity.Application.Login;

/// <summary>
/// Sign in with an existing account.
/// </summary>
/// <param name="Email">The address as the user typed it. Normalised by the handler before lookup.</param>
/// <param name="Password">The plaintext password.</param>
public sealed record LoginUser(string Email, string Password);
