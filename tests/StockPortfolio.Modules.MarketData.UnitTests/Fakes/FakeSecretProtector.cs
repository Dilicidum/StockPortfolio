using System.Text;

using StockPortfolio.Modules.MarketData.Application.Abstractions;

namespace StockPortfolio.Tests.Fakes;

/// <summary>Base64-encodes with a prefix rather than echoing the plaintext back unchanged. A plain
/// string concatenation would still leave the plaintext sitting inside the ciphertext as a literal
/// substring — this is why the prefix alone is not enough, and the bytes have to actually change.</summary>
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
