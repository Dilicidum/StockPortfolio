using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.Application.Abstractions;

/// <summary>
/// The outcome of trying to add a user.
/// </summary>
public enum AddUserOutcome
{
    /// <summary>The user was inserted.</summary>
    Added = 0,

    /// <summary>Another row already holds that email address.</summary>
    EmailTaken = 1,
}

/// <summary>
/// Stores and finds users.
/// </summary>
public interface IUserRepository
{
    /// <summary>Finds a user by their canonical address.</summary>
    /// <param name="normalisedEmail">The address, already trimmed and lower-cased by the caller.</param>
    /// <param name="ct">Cancels the operation.</param>
    /// <returns>The user, or <see langword="null"/> when no account has that address.</returns>
    Task<User?> FindByEmailAsync(string normalisedEmail, CancellationToken ct);

    /// <summary>Finds a user by id.</summary>
    /// <param name="id">The user id, normally read from the <c>sub</c> claim.</param>
    /// <param name="ct">Cancels the operation.</param>
    /// <returns>The user, or <see langword="null"/> when the row has gone.</returns>
    Task<User?> FindByIdAsync(UserId id, CancellationToken ct);

    /// <summary>Inserts a user, reporting a duplicate address rather than throwing.</summary>
    /// <param name="user">The user to insert.</param>
    /// <param name="ct">Cancels the operation.</param>
    /// <returns><see cref="AddUserOutcome.EmailTaken"/> when the unique index rejected the row.</returns>
    /// <remarks>
    /// <para>
    /// Returning an outcome rather than throwing is deliberate, and so is where the detection
    /// happens. Recognising a duplicate means reading Postgres SQLSTATE <c>23505</c> off a
    /// <c>PostgresException</c>, and this project must not reference the driver — so the
    /// implementation catches it and returns something provider-neutral.
    /// </para>
    /// <para>
    /// Callers must <b>not</b> pre-<c>SELECT</c> to check for a duplicate first. That is a race: two
    /// registrations for the same address can both see nothing and both proceed. The unique index
    /// is the only real guarantee, so the insert is the check.
    /// </para>
    /// </remarks>
    Task<AddUserOutcome> AddAsync(User user, CancellationToken ct);
}
