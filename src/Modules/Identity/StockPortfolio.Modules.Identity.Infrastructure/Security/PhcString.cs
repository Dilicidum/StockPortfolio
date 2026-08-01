using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace StockPortfolio.Modules.Identity.Infrastructure.Security;

/// <summary>
/// The PHC string format for an argon2id hash:
/// <c>$argon2id$v=19$m=19456,t=2,p=1$&lt;b64salt&gt;$&lt;b64hash&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// Storing the parameters alongside the digest is what makes the cost factors upgradable. Verification
/// re-derives with the parameters that produced the stored hash, so raising <c>m</c> or <c>t</c> later
/// does not invalidate anyone's password — a login can compare the stored parameters against the current
/// ones and transparently rehash. A bare digest column forecloses that.
/// </para>
/// <para>
/// Base64 here is the standard alphabet with padding stripped, per the PHC specification.
/// </para>
/// </remarks>
internal sealed record PhcString(int MemoryKib, int Iterations, int Parallelism, byte[] Salt, byte[] Hash)
{
    internal const string AlgorithmId = "argon2id";

    /// <summary>0x13 — the only argon2 version anything still emits.</summary>
    internal const int Version = 19;

    // Bounds on parse. A stored hash is trusted input, but a corrupted or attacker-supplied row must not
    // be able to ask for a 16 GiB derivation; the parse is the only place to say so.
    private const int MinMemoryKib = 8;
    private const int MaxMemoryKib = 1_048_576;
    private const int MinIterations = 1;
    private const int MaxIterations = 16;
    private const int MinParallelism = 1;
    private const int MaxParallelism = 16;
    private const int MinSaltLength = 8;
    private const int MaxSaltLength = 64;
    private const int MinHashLength = 16;
    private const int MaxHashLength = 64;

    private const int ExpectedSegmentCount = 6;

    /// <summary>Renders this instance as a canonical PHC string.</summary>
    public string Format() => string.Create(
        CultureInfo.InvariantCulture,
        $"${AlgorithmId}$v={Version}$m={MemoryKib},t={Iterations},p={Parallelism}${Encode(Salt)}${Encode(Hash)}");

    /// <summary>
    /// Parses a PHC string. Returns <see langword="false"/> for anything malformed rather than throwing:
    /// the input is a database column, and a bad row must fail a login, not the process.
    /// </summary>
    public static bool TryParse(string? value, [NotNullWhen(true)] out PhcString? result)
    {
        result = null;

        if (string.IsNullOrEmpty(value) || value[0] != '$')
        {
            return false;
        }

        // A leading '$' produces an empty first segment, so a well-formed string has six.
        var segments = value.Split('$');
        if (segments.Length != ExpectedSegmentCount)
        {
            return false;
        }

        if (!string.Equals(segments[1], AlgorithmId, StringComparison.Ordinal))
        {
            return false;
        }

        if (!TryParseTaggedInt(segments[2], "v=", out var version) || version != Version)
        {
            return false;
        }

        var parameters = segments[3].Split(',');
        if (parameters.Length != 3
            || !TryParseTaggedInt(parameters[0], "m=", out var memoryKib)
            || !TryParseTaggedInt(parameters[1], "t=", out var iterations)
            || !TryParseTaggedInt(parameters[2], "p=", out var parallelism))
        {
            return false;
        }

        if (memoryKib is < MinMemoryKib or > MaxMemoryKib
            || iterations is < MinIterations or > MaxIterations
            || parallelism is < MinParallelism or > MaxParallelism)
        {
            return false;
        }

        if (!TryDecode(segments[4], MinSaltLength, MaxSaltLength, out var salt)
            || !TryDecode(segments[5], MinHashLength, MaxHashLength, out var hash))
        {
            return false;
        }

        result = new PhcString(memoryKib, iterations, parallelism, salt, hash);
        return true;
    }

    private static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=');

    private static bool TryParseTaggedInt(string segment, string tag, out int value)
    {
        value = 0;

        if (!segment.StartsWith(tag, StringComparison.Ordinal))
        {
            return false;
        }

        // NumberStyles.None rejects a leading sign, whitespace and thousands separators, so "+2" and
        // " 2" are malformed rather than quietly accepted.
        return int.TryParse(
            segment.AsSpan(tag.Length),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out value);
    }

    private static bool TryDecode(string segment, int minLength, int maxLength, out byte[] value)
    {
        value = [];

        // Unpadded base64 can be 0, 2 or 3 characters short of a multiple of four; one short is
        // impossible, and Convert would reject it anyway.
        var padding = (4 - (segment.Length % 4)) % 4;
        if (segment.Length == 0 || padding == 3)
        {
            return false;
        }

        var padded = segment + new string('=', padding);
        var buffer = new byte[padded.Length / 4 * 3];

        if (!Convert.TryFromBase64String(padded, buffer, out var written)
            || written < minLength
            || written > maxLength)
        {
            return false;
        }

        value = buffer[..written];
        return true;
    }
}
