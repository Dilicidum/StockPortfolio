using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.Application.Abstractions;

/// <summary>Stores and finds users.</summary>
public interface IUserRepository
{
    /// <summary>Finds a user by their canonical address.</summary>
    Task<User?> FindByEmailAsync(string normalisedEmail, CancellationToken ct);

    /// <summary>Finds a user by id.</summary>
    Task<User?> FindByIdAsync(UserId id, CancellationToken ct);

    /// <summary>Inserts a user and commits.</summary>
    Task AddAsync(User user, CancellationToken ct);
}
