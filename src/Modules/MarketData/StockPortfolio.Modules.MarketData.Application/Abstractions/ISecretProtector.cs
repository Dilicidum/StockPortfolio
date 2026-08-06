namespace StockPortfolio.Modules.MarketData.Application.Abstractions;

/// <summary>Scrambles a secret before it is stored. The host decides how.</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);

    /// <summary>Null when the stored value cannot be read back: a lost key ring, or tampering.</summary>
    string? Unprotect(string ciphertext);
}
