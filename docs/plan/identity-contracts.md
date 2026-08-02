# Identity module — frozen contracts

**This file is the contract three parallel agents build against. Do not change a signature here without saying so loudly — another project is compiling against it.**

Namespaces are exact. Every type must live in one.

Accessibility per [phase-1-implementation.md](phase-1-implementation.md) §4.2 — `Domain`, `Application` and `Api` are `public`; `Infrastructure` is `internal` except `IdentityModule`.

---

## Already built — `StockPortfolio.Shared.Kernel`

Framework-free, and deliberately small: `Money` and the CQRS interfaces. There is no `AggregateRoot<TId>` and no `IDomainEvent` — both were written, found to be carrying nothing, and deleted. Phase 2 brings an event type back, at `HoldingRemoved` — the first one anything actually raises.

```csharp
namespace StockPortfolio.Shared.Kernel;

public readonly record struct Money(decimal Amount, string Currency)
{
    public const string UsdCurrencyCode = "USD";
    public static Money Usd(decimal amount);
    public static Money Zero(string currency);
    public Money Add(Money other);        // throws InvalidOperationException on currency mismatch
    public Money Subtract(Money other);
    public Money Multiply(decimal factor);
    public static Money operator +(Money left, Money right);
    public static Money operator -(Money left, Money right);
    public static Money operator *(Money left, decimal right);
}
```

```csharp
namespace StockPortfolio.Shared.Kernel.Cqrs;

public interface ICommandHandler<in TCommand, TResult> { Task<TResult> Handle(TCommand command, CancellationToken ct); }
public interface IQueryHandler<in TQuery, TResult>     { Task<TResult> Handle(TQuery query, CancellationToken ct); }

public sealed record InvalidInput(string Field, string Message);
```

`InvalidInput` is the one failure case shared across modules, because every layer can produce a field-plus-message. Everything else is named per use case.

```csharp
namespace StockPortfolio.Shared.Api;

public sealed class ValidationFilter<TRequest>(IValidator<TRequest> validator) : IEndpointFilter
    where TRequest : class;

public static class ProblemDetailsExtensions
{
    public static ValidationProblem ToValidationProblem(this InvalidInput failure);   // 400
    public static ProblemHttpResult ConflictProblem(string detail);                   // 409
    public static ProblemHttpResult UnauthorizedProblem(string detail);               // 401
}
```

There is no `IEndpointModule`. An interface with one method that every module implements once, and that the host calls once per module, is a registration list written twice — the modules expose plain `MapXxxEndpoints` extension methods and the host calls them directly.

`NotFoundProblem` and `ForbiddenProblem` do not exist yet. Add them when a route actually returns 404 or 403, not before — an unused helper reads as a supported status.

---

## `StockPortfolio.Modules.Identity.Domain`

```csharp
namespace StockPortfolio.Modules.Identity.Domain;

public readonly record struct UserId(Guid Value)
{
    public static UserId New();              // Guid.CreateVersion7() - UUIDv7 for index locality
}

public readonly record struct RefreshTokenId(Guid Value) { public static RefreshTokenId New(); }

public sealed class User
{
    private User(UserId id, string email, string passwordHash, DateTimeOffset createdAt);

    public UserId Id { get; private set; }
    public string Email { get; private set; }
    public string PasswordHash { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static string NormaliseEmail(string? email);
    public static OneOf<User, InvalidInput> Create(string email, string passwordHash, TimeProvider clock);
    public void ChangePasswordHash(string newHash);   // throws on null/empty
}

public sealed class RefreshToken
{
    private RefreshToken(
        RefreshTokenId id, UserId userId, byte[] tokenHash, DateTimeOffset expiresAt,
        DateTimeOffset createdAt, DateTimeOffset? supersededAt, RefreshTokenId? supersededBy);

    public RefreshTokenId Id { get; private set; }
    public UserId UserId { get; private set; }
    public byte[] TokenHash { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SupersededAt { get; private set; }
    public RefreshTokenId? SupersededBy { get; private set; }

    public bool IsActive(TimeProvider clock);
    public static RefreshToken Issue(UserId userId, byte[] tokenHash, DateTimeOffset expiresAt, TimeProvider clock);
    public void Supersede(RefreshToken replacement, TimeProvider clock);   // THROWS if already superseded
    public void Revoke(TimeProvider clock);                               // THROWS if already superseded
}
```

**No base class, and exactly one constructor.** Each entity declares its own `Id`. The single constructor is private, takes every mapped value, and assigns — nothing else. No parameterless constructor, no object initialiser, no public setter, so a half-built entity is not representable and the static factory is the only way in.

EF binds that constructor by **parameter name** and does not care that it is private, so it is what materialises every row. That is exactly why it must stay guard-free: a guard placed inside it runs on every row of every `SELECT`. Validation lives in the factory, which EF never calls. Rename a constructor parameter without renaming its property and EF finds no bindable constructor — with no parameterless fallback, the whole model fails to build at startup rather than on first query. `EfConstructorBindingTests` pins this.

**`Supersede` and `Revoke` are different endings.** Both stamp `SupersededAt`; only `Supersede` sets `SupersededBy`. Anything reasoning about the rotation grace period must key off `SupersededBy`, or logging out silently does nothing for the length of the window.

`User.Create` normalises the email through `User.NormaliseEmail` and returns `InvalidInput("email", …)` on a malformed address. `NormaliseEmail` is public and is the single definition of the canonical form — handlers use it to look up by address, because a lookup that normalises differently from what was stored simply misses.

**There is no `Errors.cs`.** A shared bag of marker records puts `EmailAlreadyUsed` in front of everyone who will never return it. Each failure record lives beside the use case that returns it, in `.Application`. `Success` and `NotFound` are not declared at all — `OneOf.Types` ships both.

---

## `StockPortfolio.Modules.Identity.Application`

```csharp
namespace StockPortfolio.Modules.Identity.Application;

public sealed record TokenPair(string AccessToken, string RefreshToken, DateTimeOffset AccessExpiresAt);

public static class TokenPolicy      // values are the repo owner's call - see phase-1-implementation.md §11
{
    public static TimeSpan AccessTokenLifetime  { get; }   // 15 minutes, provisional
    public static TimeSpan RefreshTokenLifetime { get; }   // 14 days, provisional
    public static bool     RotateOnUse          { get; }   // true
    public static TimeSpan RotationGracePeriod  { get; }   // 30 seconds, provisional
}
```

```csharp
namespace StockPortfolio.Modules.Identity.Application.Abstractions;

public interface IPasswordHasher
{
    string DummyHash { get; }        // for constant-time login when the user does not exist
    string Hash(string password);
    bool Verify(string password, string encodedHash);
}

public interface ITokenIssuer
{
    string IssueAccessToken(UserId userId, string email, DateTimeOffset expiresAt);
    string NewRefreshToken();                       // 32 random bytes, base64url
    byte[] HashRefreshToken(string refreshToken);   // SHA-256
}

public interface IUserRepository
{
    Task<User?> FindByEmailAsync(string normalisedEmail, CancellationToken ct);
    Task<User?> FindByIdAsync(UserId id, CancellationToken ct);
    Task AddAsync(User user, CancellationToken ct);
}

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> FindByHashAsync(byte[] tokenHash, CancellationToken ct);
    Task AddAsync(RefreshToken token, CancellationToken ct);
    Task UpdateAsync(RefreshToken token, CancellationToken ct);
}
```

**There is no `IUnitOfWork`.** `DbContext` already is one, and a second unit of work over it adds a name and nothing else. Every write method above persists before it returns. Because all of a module's repositories share one scoped `DbContext`, a single commit carries whatever else the handler changed — so where two writes must land together, order them so the last repository call is the one that commits. `RefreshSessionCommandHandler` supersedes the old session *before* calling `AddAsync` for the replacement, so one transaction covers both.

**"Is this address taken?" is asked in the handler, not read out of an exception.** `RegisterUserCommandHandler` calls `FindByEmailAsync(User.NormaliseEmail(command.Email))` and returns `EmailAlreadyUsed`. It does this *before* hashing, since Argon2id is deliberately slow and a taken address is a 409 whatever the password was.

The earlier design had `AddAsync` return an `AddUserOutcome` enum by catching SQLSTATE `23505` inside the repository. It worked and it was race-free, but the rule then lived in `.Infrastructure` and the handler never mentioned it — which contradicts the rule that context questions are answered in `.Application` as a result case. Accepted cost of the change: two simultaneous registrations of one address can both pass the check, and the loser hits the unique index and surfaces as **500 rather than 409**. The index stays — it is what keeps the data correct — and the window is milliseconds. Reintroduce the catch only if that 500 is ever actually observed.

### Use cases — one folder each: command, handler, and the records the outcome needs

A handler returns `OneOf<…>` of its outcomes **directly**. No `[GenerateOneOf]`, no named union class. `<UseCase>Result` is the success payload record, never the union; several use cases share `TokenPair` and so declare no result type of their own.

```csharp
namespace StockPortfolio.Modules.Identity.Application.Authentication.Commands.RegisterUser;
public sealed record RegisterUserCommand(string Email, string Password);
public sealed record EmailAlreadyUsed;
public sealed class RegisterUserCommandHandler
    : ICommandHandler<RegisterUserCommand, OneOf<TokenPair, EmailAlreadyUsed, InvalidInput>>;

namespace StockPortfolio.Modules.Identity.Application.Authentication.Commands.LoginUser;
public sealed record LoginUserCommand(string Email, string Password);
public sealed record InvalidCredentials;
public sealed class LoginUserCommandHandler
    : ICommandHandler<LoginUserCommand, OneOf<TokenPair, InvalidCredentials>>;

namespace StockPortfolio.Modules.Identity.Application.Authentication.Commands.RefreshSession;
public sealed record RefreshSessionCommand(string RefreshToken);      // NOT RefreshToken - CS0542
public sealed record InvalidOrExpired;
public sealed class RefreshSessionCommandHandler
    : ICommandHandler<RefreshSessionCommand, OneOf<TokenPair, InvalidOrExpired>>;

namespace StockPortfolio.Modules.Identity.Application.Authentication.Commands.RevokeSession;
public sealed record RevokeSessionCommand(string RefreshToken);
public sealed class RevokeSessionCommandHandler
    : ICommandHandler<RevokeSessionCommand, OneOf<Success, NotFound>>;          // OneOf.Types

namespace StockPortfolio.Modules.Identity.Application.Authentication.Queries.GetCurrentUser;
public sealed record GetCurrentUserQuery(Guid UserId);
public sealed record GetCurrentUserResult(Guid Id, string Email);
public sealed class GetCurrentUserQueryHandler
    : IQueryHandler<GetCurrentUserQuery, OneOf<GetCurrentUserResult, NotFound>>;
```

Exhaustiveness is structural: `.Match` takes one delegate per case, so adding a case breaks every call site. Name every `.Match` lambda parameter — `emailTaken =>`, not `_ =>`.

Login has **two** cases, not three. `InvalidCredentials` is deliberately undifferentiated — separating "no such user" from "wrong password" leaks account existence. The handler must also verify against `IPasswordHasher.DummyHash` when the user does not exist, so the timing does not leak either.

---

## `StockPortfolio.Modules.Identity.Infrastructure`

Everything `internal` **except**:

```csharp
namespace StockPortfolio.Modules.Identity.Infrastructure;

public static class IdentityModule
{
    public const string ConnectionStringName = "Identity";
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration config);
}
```

Registers `IdentityDbContext`, the two repositories, `IPasswordHasher`, `ITokenIssuer`, and every handler.

Handler registrations spell out the closed generic, which is the cost of returning `OneOf<…>` directly and is worth paying for the visible outcome list:

```csharp
services.AddScoped<
    ICommandHandler<RegisterUserCommand, OneOf<TokenPair, EmailAlreadyUsed, InvalidInput>>,
    RegisterUserCommandHandler>();
```

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

**Requests live here, not in `.Application`.** An Application type never binds off the wire. The endpoint reads the request and constructs the command with `new`:

```csharp
namespace StockPortfolio.Modules.Identity.Api.Requests;

public sealed record RegisterUserRequest(string Email, string Password);
public sealed record LoginUserRequest(string Email, string Password);
public sealed record RefreshSessionRequest(string RefreshToken);
public sealed record RevokeSessionRequest(string RefreshToken);
```

Validators sit in `Validators/` as `<UseCase>RequestValidator` and validate the **request**, so `ValidationFilter<T>` closes over the request type. Only these records reach `/openapi/v1.json`.

**Endpoint handlers return `Task<IResult>`**, not a `Results<Created<T>, ProblemHttpResult, …>` union — the typed union restates what `.Produces(...)` already declares and grows an argument per case. `.Match<IResult>(…)` keeps exhaustiveness, which comes from the union's arity rather than the return type. The trade: `.Produces(...)` metadata is now the only description of what a route emits, so verify it against a live response and read it back from `/openapi/v1.json`.

Wire format — the frontend is already coded against exactly this:

```
POST /api/auth/register  {email,password} -> 201 {accessToken,refreshToken,accessExpiresAt} | 409 | 400
POST /api/auth/login     {email,password} -> 200 same shape | 401
POST /api/auth/refresh   {refreshToken}   -> 200 same shape | 401
POST /api/auth/logout    bearer           -> 204
GET  /api/auth/me        bearer           -> 200 {id,email}
```

Every route also declares 415 and 500 as `problem+json`, which is truthful only because `AddProblemDetails()` and `UseStatusCodePages()` are both registered in the host.

`TokenPair` serialises with camelCase property names: `accessToken`, `refreshToken`, `accessExpiresAt`.

The user id comes from the `sub` claim. `MapInboundClaims = false` is set on `JwtBearerOptions` in the host, so read `"sub"` directly — with the default `true` it would have been renamed to the long Microsoft claim URI and `FindFirst("sub")` would silently return null.
