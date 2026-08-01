using Microsoft.EntityFrameworkCore;

using Npgsql;

using StockPortfolio.Modules.Identity.Application.Abstractions;
using StockPortfolio.Modules.Identity.Domain;
using StockPortfolio.Modules.Identity.Infrastructure.Persistence.Configurations;

namespace StockPortfolio.Modules.Identity.Infrastructure.Persistence;

/// <inheritdoc cref="IUserRepository"/>
internal sealed class UserRepository(IdentityDbContext context) : IUserRepository
{
    public async Task<User?> FindByEmailAsync(string normalisedEmail, CancellationToken ct)
        => await context.Users
            .FirstOrDefaultAsync(u => u.Email == normalisedEmail, ct)
            .ConfigureAwait(false);

    public async Task<User?> FindByIdAsync(UserId id, CancellationToken ct)
        => await context.Users
            .FirstOrDefaultAsync(u => u.Id == id, ct)
            .ConfigureAwait(false);

    /// <remarks>
    /// <para>
    /// This is the one repository method that saves. Detecting a duplicate email means letting the
    /// unique index reject the insert, so the insert has to reach the database here rather than at the
    /// unit of work's <c>SaveChanges</c>. A pre-<c>SELECT</c> would be a race, and the index is the only
    /// real guarantee.
    /// </para>
    /// <para>
    /// Recognising the violation needs <see cref="PostgresException"/>, and <c>.Application</c> must not
    /// reference the driver — hence the provider-neutral <see cref="AddUserOutcome"/>. The match is
    /// narrowed to the email index by name: any other unique violation is a bug, not a taken email, and
    /// must keep propagating.
    /// </para>
    /// </remarks>
    public async Task<AddUserOutcome> AddAsync(User user, CancellationToken ct)
    {
        context.Users.Add(user);

        try
        {
            await context.SaveChangesAsync(ct).ConfigureAwait(false);
            return AddUserOutcome.Added;
        }
        catch (DbUpdateException ex) when (IsEmailUniqueViolation(ex))
        {
            // The entity is still Added after a failed save; leaving it there would replay the same
            // failing INSERT on the next SaveChanges in this scope.
            context.Entry(user).State = EntityState.Detached;
            return AddUserOutcome.EmailTaken;
        }
    }

    private static bool IsEmailUniqueViolation(DbUpdateException exception)
        => exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
            && string.Equals(pg.ConstraintName, UserConfiguration.EmailUniqueIndexName, StringComparison.Ordinal);
}
