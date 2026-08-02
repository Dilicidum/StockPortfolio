using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.Application.Abstractions;

/// <summary>Stores and finds login sessions.</summary>
public interface IRefreshTokenRepository
{
    /// <summary>Finds a session by the hash of its token.</summary>
    Task<RefreshToken?> FindByHashAsync(byte[] tokenHash, CancellationToken ct);

    /// <summary>Adds a session.</summary>
    Task AddAsync(RefreshToken token, CancellationToken ct);
}
