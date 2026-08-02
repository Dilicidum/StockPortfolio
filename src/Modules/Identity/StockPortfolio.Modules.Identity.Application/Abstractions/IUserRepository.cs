using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.Application.Abstractions;

/// <summary>The outcome of trying to add a user.</summary>
public enum AddUserOutcome
{
    /// <summary>The user was inserted.</summary>
    Added = 0,

    /// <summary>Another row already holds that email address.</summary>
    EmailTaken = 1,
}

/// <summary>Stores and finds users.</summary>
public interface IUserRepository
{
    /// <summary>Finds a user by their canonical address.</summary>
    Task<User?> FindByEmailAsync(string normalisedEmail, CancellationToken ct);

    /// <summary>Finds a user by id.</summary>
    Task<User?> FindByIdAsync(UserId id, CancellationToken ct);

    /// <summary>Inserts a user, reporting a duplicate address rather than throwing.</summary>
    Task<AddUserOutcome> AddAsync(User user, CancellationToken ct);
}
