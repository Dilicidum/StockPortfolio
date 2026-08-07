using System.Security.Cryptography;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

using StockPortfolio.Modules.MarketData.Application.Abstractions;

namespace StockPortfolio.Host.Adapters;

internal sealed partial class DataProtectionSecretProtector(
    IDataProtectionProvider provider,
    ILogger<DataProtectionSecretProtector> logger) : ISecretProtector
{
    // Part of the ciphertext itself. Changing this string later makes every stored key unreadable.
    private readonly IDataProtector protector = provider.CreateProtector("StockPortfolio.MarketData.UserProviderKey");

    public string Protect(string plaintext) => protector.Protect(plaintext);

    public string? Unprotect(string ciphertext)
    {
        try
        {
            return protector.Unprotect(ciphertext);
        }
        catch (CryptographicException ex)
        {
            // Never silent - a dead key otherwise reads as "nothing configured" forever - and never logs the ciphertext or any key material.
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
