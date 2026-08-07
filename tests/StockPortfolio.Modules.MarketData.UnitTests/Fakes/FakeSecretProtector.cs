using System.Text;

using StockPortfolio.Modules.MarketData.Application.Abstractions;

namespace StockPortfolio.Tests.Fakes;

// Base64, not concatenation: a prefixed plaintext would still sit inside the ciphertext as a literal substring.
internal sealed class FakeSecretProtector : ISecretProtector
{
    private const string Prefix = "protected:";

    public string Protect(string plaintext) => Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));

    public string? Unprotect(string ciphertext)
    {
        if (!ciphertext.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(ciphertext[Prefix.Length..]));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
