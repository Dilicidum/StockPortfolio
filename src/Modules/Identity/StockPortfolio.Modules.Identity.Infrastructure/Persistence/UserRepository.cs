using Microsoft.EntityFrameworkCore;

using Npgsql;

using StockPortfolio.Modules.Identity.Application.Abstractions;
using StockPortfolio.Modules.Identity.Domain;
using StockPortfolio.Modules.Identity.Infrastructure.Persistence.Configurations;

namespace StockPortfolio.Modules.Identity.Infrastructure.Persistence;

internal sealed class UserRepository(IdentityDbContext context) : IUserRepository
{
    public async Task<User?> FindByEmailAsync(string normalisedEmail, CancellationToken ct)
        => await context.Users
            .FirstOrDefaultAsync(u => u.Email == normalisedEmail, ct);

    public async Task<User?> FindByIdAsync(UserId id, CancellationToken ct)
        => await context.Users
            .FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task<AddUserOutcome> AddAsync(User user, CancellationToken ct)
    {
        context.Users.Add(user);

        try
        {
            await context.SaveChangesAsync(ct);
            return AddUserOutcome.Added;
        }
        catch (DbUpdateException ex) when (IsEmailUniqueViolation(ex))
        {
            // The entity is still Added after a failed save; leaving it there would replay the same failing.
            context.Entry(user).State = EntityState.Detached;
            return AddUserOutcome.EmailTaken;
        }
    }

    private static bool IsEmailUniqueViolation(DbUpdateException exception)
        => exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
            && string.Equals(pg.ConstraintName, UserConfiguration.EmailUniqueIndexName, StringComparison.Ordinal);
}
