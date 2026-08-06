using System.Security.Cryptography;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

using StockPortfolio.Modules.MarketData.Application.Abstractions;

namespace StockPortfolio.Api.Adapters;

/// <summary>Protects a secret through the framework's key ring, so MarketData never sees ASP.NET Core.</summary>
internal sealed partial class DataProtectionSecretProtector(
    IDataProtectionProvider provider,
    ILogger<DataProtectionSecretProtector> logger) : ISecretProtector
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
        catch (CryptographicException ex)
        {
            // A rotated-away key ring, or a tampered row. Neither is recoverable and neither is an outage -
            // but it must not be silent, or a dead key reads identically to "nothing configured" forever.
            // Never the ciphertext or any key material: only the fact that decryption failed.
            LogUnprotectFailed(logger, ex);
            return null;
        }
    }

    [LoggerMessage(
        EventId = 5300,
        Level = LogLevel.Warning,
        Message = "Failed to decrypt a stored provider key; its key ring entry may have rotated away or the row was tampered with")]
    private static partial void LogUnprotectFailed(ILogger logger, Exception exception);
}
