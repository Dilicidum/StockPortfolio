namespace StockPortfolio.Modules.MarketData.Application.Abstractions;

public interface ISecretProtector
{
    string Protect(string plaintext);

    string? Unprotect(string ciphertext);
}
