namespace StockPortfolio.Modules.Identity.Application.Register;

/// <summary>
/// Create an account and sign in with it in one step.
/// </summary>
/// <param name="Email">The address to register. Normalised by the domain, not here.</param>
/// <param name="Password">The plaintext password. Hashed before it touches anything persistent.</param>
/// <remarks>
/// Shape — is this even an email, is the password long enough — has already been checked by the
/// endpoint filter by the time a handler sees this command. What is left is context and invariant.
/// </remarks>
public sealed record RegisterUser(string Email, string Password);
