# Identity module — frozen contracts

**This file is the contract three parallel agents build against. Do not change a signature here without saying so loudly — another project is compiling against it.**

Namespaces are exact. Every type must live in one: `[GenerateOneOf]` crashes with `CS8785` on the global namespace.

Accessibility per [phase-1-implementation.md](phase-1-implementation.md) §4.2 — `Domain`, `Application` and `Presentation` are `public`; `Infrastructure` is `internal` except `IdentityModule`.

---

## Already built — `StockPortfolio.Shared.Kernel`

```csharp
namespace StockPortfolio.Shared.Kernel;

public interface IDomainEvent { DateTimeOffset OccurredAt { get; } }

public abstract class AggregateRoot<TId> where TId : struct
{
    public TId Id { get; protected set; }
    [NotMapped] public IReadOnlyCollection<IDomainEvent> DomainEvents { get; }
    protected void Raise(IDomainEvent e);
    public void ClearDomainEvents();
}

public readonly record struct Money(decimal Amount, string Currency)
{
    public const string UsdCurrencyCode = "USD";
    public static Money Usd(decimal amount);
    public static Money Zero(string currency);
    public Money Add(Money other);        // throws InvalidOperationException on currency mismatch
    public Money Subtract(Money other);
    public Money Multiply(decimal factor);
}
```

```csharp
namespace StockPortfolio.Shared.Kernel.Cqrs;

public interface ICommandHandler<in TCommand, TResult> { Task<TResult> Handle(TCommand command, CancellationToken ct); }
public interface IQueryHandler<in TQuery, TResult>     { Task<TResult> Handle(TQuery query, CancellationToken ct); }
public sealed record ValidationFailed(string Field, string Message);
```

```csharp
namespace StockPortfolio.Shared.Api;

public interface IEndpointModule { void MapEndpoints(IEndpointRouteBuilder app); }

public sealed class ValidationFilter<TRequest>(IValidator<TRequest> validator) : IEndpointFilter
    where TRequest : class;

public static class ProblemDetailsExtensions
{
    public static ValidationProblem ToValidationProblem(this ValidationFailed failure); // 400
    public static ProblemHttpResult NotFoundProblem(string detail);                     // 404
    public static ProblemHttpResult ConflictProblem(string detail);                     // 409
    public static ProblemHttpResult UnauthorizedProblem(string detail);                 // 401
    public static ProblemHttpResult ForbiddenProblem(string detail);                    // 403
}
```

---

## `StockPortfolio.Modules.Identity.Domain`

```csharp
namespace StockPortfolio.Modules.Identity.Domain;

public readonly record struct UserId(Guid Value)
{
    public static UserId New();              // Guid.CreateVersion7() - UUIDv7 for index locality
}

public sealed class User : AggregateRoot<UserId>
{
    private User();                          // EF only. No validation. Id is NOT re-declared (CS0108).
    public string Email { get; private set; }
    public string PasswordHash { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static OneOf<User, ValidationFailed> Create(string email, string passwordHash, TimeProvider clock);
    public void ChangePasswordHash(string newHash);   // throws on null/empty
}

public sealed class RefreshToken : AggregateRoot<RefreshTokenId>
{
    private RefreshToken();
    public UserId UserId { get; private set; }
    public byte[] TokenHash { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SupersededAt { get; private set; }
    public RefreshTokenId? SupersededBy { get; private set; }

    public bool IsActive(TimeProvider clock);
    public static RefreshToken Issue(UserId userId, byte[] tokenHash, DateTimeOffset expiresAt, TimeProvider clock);
    public void Supersede(RefreshToken replacement, TimeProvider clock);   // THROWS if already superseded
}

public readonly record struct RefreshTokenId(Guid Value) { public static RefreshTokenId New(); }

// Errors.cs - the named failure cases. Marker records, no members.
public sealed record EmailAlreadyUsed;
public sealed record InvalidCredentials;
public sealed record InvalidOrExpired;
public sealed record SessionNotFound;
public sealed record Success;
```

`User.Create` normalises email to trimmed lowercase and returns `ValidationFailed("email", …)` on a malformed address. Construct with an object initialiser inside `Create` — **never** add a constructor whose parameter names match mapped properties, EF's binder will hijack it for materialisation and run the guards on every `SELECT`.

---

## `StockPortfolio.Modules.Identity.Application`

```csharp
namespace StockPortfolio.Modules.Identity.Application;

public sealed record TokenPair(string AccessToken, string RefreshToken, DateTimeOffset AccessExpiresAt);

public static class TokenPolicy      // values are the repo owner's call - see phase-1-implementation.md §11
{
    public static TimeSpan AccessTokenLifetime  { get; }
    public static TimeSpan RefreshTokenLifetime { get; }
    public static bool     RotateOnUse          { get; }
    public static TimeSpan RotationGracePeriod  { get; }
}
```

```csharp
namespace StockPortfolio.Modules.Identity.Application.Abstractions;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string encodedHash);
    string DummyHash { get; }        // for constant-time login when the user does not exist
}

public interface ITokenIssuer
{
    string IssueAccessToken(UserId userId, string email, DateTimeOffset expiresAt);
    string NewRefreshToken();                       // 32 random bytes, base64url
    byte[] HashRefreshToken(string refreshToken);   // SHA-256
}

public enum AddUserOutcome { Added, EmailTaken }

public interface IUserRepository
{
    Task<User?> FindByEmailAsync(string normalisedEmail, CancellationToken ct);
    Task<User?> FindByIdAsync(UserId id, CancellationToken ct);
    Task<AddUserOutcome> AddAsync(User user, CancellationToken ct);   // catches 23505 internally
}

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> FindByHashAsync(byte[] tokenHash, CancellationToken ct);
    Task AddAsync(RefreshToken token, CancellationToken ct);
}

public interface IUnitOfWork { Task SaveChangesAsync(CancellationToken ct); }
```

**`AddAsync` returning `AddUserOutcome` is deliberate.** Detecting a duplicate email means catching Postgres SQLSTATE `23505`, which needs `Npgsql.PostgresException` — and `.Application` must not reference the driver. The repository (in `.Infrastructure`) catches it and returns a provider-neutral outcome. Do **not** pre-`SELECT` to check: that is a race, and the unique index is the only real guarantee.

### Use cases — one folder each, three files: command, result union, handler

```csharp
namespace StockPortfolio.Modules.Identity.Application.Authentication.Commands.RegisterUser;
public sealed record RegisterUserCommand(string Email, string Password);
[GenerateOneOf] public partial class RegisterUserResult : OneOfBase<TokenPair, EmailAlreadyUsed, ValidationFailed>;
public sealed class RegisterUserCommandHandler : ICommandHandler<RegisterUserCommand, RegisterUserResult>;

namespace StockPortfolio.Modules.Identity.Application.Authentication.Commands.LoginUser;
public sealed record LoginUserCommand(string Email, string Password);
[GenerateOneOf] public partial class LoginUserResult : OneOfBase<TokenPair, InvalidCredentials>;
public sealed class LoginUserCommandHandler : ICommandHandler<LoginUserCommand, LoginUserResult>;

namespace StockPortfolio.Modules.Identity.Application.Authentication.Commands.RefreshSession;
public sealed record RefreshSessionCommand(string RefreshToken);      // NOT RefreshToken - CS0542
[GenerateOneOf] public partial class RefreshSessionResult : OneOfBase<TokenPair, InvalidOrExpired>;
public sealed class RefreshSessionCommandHandler : ICommandHandler<RefreshSessionCommand, RefreshSessionResult>;

namespace StockPortfolio.Modules.Identity.Application.Authentication.Commands.RevokeSession;
public sealed record RevokeSessionCommand(string RefreshToken);
[GenerateOneOf] public partial class RevokeSessionResult : OneOfBase<Success, SessionNotFound>;
public sealed class RevokeSessionCommandHandler : ICommandHandler<RevokeSessionCommand, RevokeSessionResult>;

namespace StockPortfolio.Modules.Identity.Application.Authentication.Queries.GetCurrentUser;
public sealed record GetCurrentUserQuery(Guid UserId);
public sealed record UserSummary(Guid Id, string Email);
[GenerateOneOf] public partial class GetCurrentUserResult : OneOfBase<UserSummary, SessionNotFound>;
public sealed class GetCurrentUserQueryHandler : IQueryHandler<GetCurrentUserQuery, GetCurrentUserResult>;
```

`LoginUserResult` has **two** cases, not three. `InvalidCredentials` is deliberately undifferentiated — separating "no such user" from "wrong password" leaks account existence. The handler must also verify against `IPasswordHasher.DummyHash` when the user does not exist, so the timing does not leak either.

---

## `StockPortfolio.Modules.Identity.Infrastructure`

Everything `internal` **except**:

```csharp
namespace StockPortfolio.Modules.Identity.Infrastructure;

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration config);
}
```

Registers `IdentityDbContext` (connection string name `Identity`), the two repositories, `IUnitOfWork`, `IPasswordHasher`, `ITokenIssuer`, and every handler.

---

## `StockPortfolio.Modules.Identity.Api`

```csharp
namespace StockPortfolio.Modules.Identity.Api;

public static class IdentityEndpoints
{
    public static IServiceCollection AddIdentityApi(this IServiceCollection services);   // validators
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app);
}
```

Wire format — the frontend is already coded against exactly this:

```
POST /api/auth/register  {email,password} -> 201 {accessToken,refreshToken,accessExpiresAt} | 409 | 400
POST /api/auth/login     {email,password} -> 200 same shape | 401
POST /api/auth/refresh   {refreshToken}   -> 200 same shape | 401
POST /api/auth/logout    bearer           -> 204
GET  /api/auth/me        bearer           -> 200 {id,email}
```

`TokenPair` serialises with camelCase property names: `accessToken`, `refreshToken`, `accessExpiresAt`.

The user id comes from the `sub` claim. `MapInboundClaims = false` is set on `JwtBearerOptions` in the host, so read `"sub"` directly — with the default `true` it would have been renamed to the long Microsoft claim URI and `FindFirst("sub")` would silently return null.
