using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.Application.Abstractions;

/// <summary>Stores and finds login sessions. Both write methods persist before they return.</summary>
public interface IRefreshTokenRepository
{
    /// <summary>Finds a session by the hash of its token.</summary>
    Task<RefreshToken?> FindByHashAsync(byte[] tokenHash, CancellationToken ct);

    /// <summary>Inserts a session, committing anything else the handler changed in the same transaction.</summary>
    Task AddAsync(RefreshToken token, CancellationToken ct);

    /// <summary>Commits a change made to an existing session.</summary>
    Task UpdateAsync(RefreshToken token, CancellationToken ct);
}
