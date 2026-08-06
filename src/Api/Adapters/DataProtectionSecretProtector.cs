using System.Security.Cryptography;

using Microsoft.AspNetCore.DataProtection;

using StockPortfolio.Modules.MarketData.Application.Abstractions;

namespace StockPortfolio.Api.Adapters;

/// <summary>Protects a secret through the framework's key ring, so MarketData never sees ASP.NET Core.</summary>
internal sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    // Part of the ciphertext itself. Changing this string later makes every stored key unreadable.
    private readonly IDataProtector protector = provider.CreateProtector("StockPortfolio.MarketData.UserProviderKey");

    /// <inheritdoc/>
    public string Protect(string plaintext) => protector.Protect(plaintext);

    /// <inheritdoc/>
    public string? Unprotect(string ciphertext)
    {
        try
        {
            return protector.Unprotect(ciphertext);
        }
        catch (CryptographicException)
        {
            // A rotated-away key ring, or a tampered row. Neither is recoverable and neither is an outage.
            return null;
        }
    }
}
