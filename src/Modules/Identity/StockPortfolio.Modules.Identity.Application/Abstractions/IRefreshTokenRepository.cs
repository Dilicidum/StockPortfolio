using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.Application.Abstractions;

/// <summary>
/// Stores and finds login sessions.
/// </summary>
/// <remarks>
/// There is no <c>Update</c>. A session loaded through <see cref="FindByHashAsync"/> is tracked, so
/// mutating it and calling <see cref="IUnitOfWork.SaveChangesAsync"/> is the whole update path.
/// </remarks>
public interface IRefreshTokenRepository
{
    /// <summary>Finds a session by the hash of its token.</summary>
    /// <param name="tokenHash">The SHA-256 digest of the presented token.</param>
    /// <param name="ct">Cancels the operation.</param>
    /// <returns>The session, or <see langword="null"/> when no session has that hash.</returns>
    Task<RefreshToken?> FindByHashAsync(byte[] tokenHash, CancellationToken ct);

    /// <summary>Adds a session.</summary>
    /// <param name="token">The session to add.</param>
    /// <param name="ct">Cancels the operation.</param>
    /// <returns>A task that completes once the session is tracked.</returns>
    Task AddAsync(RefreshToken token, CancellationToken ct);
}
