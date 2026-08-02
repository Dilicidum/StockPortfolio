using Microsoft.EntityFrameworkCore;

using StockPortfolio.Modules.Identity.Application.Abstractions;
using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.Infrastructure.Persistence;

internal sealed class UserRepository(IdentityDbContext context) : IUserRepository
{
    public async Task<User?> FindByEmailAsync(string normalisedEmail, CancellationToken ct)
        => await context.Users.FirstOrDefaultAsync(u => u.Email == normalisedEmail, ct);

    public async Task<User?> FindByIdAsync(UserId id, CancellationToken ct)
        => await context.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task AddAsync(User user, CancellationToken ct)
    {
        context.Users.Add(user);
        await context.SaveChangesAsync(ct);
    }
}
