# Identity

Owns accounts and sign-in, and each user's theme and language. Repo-wide rules are in the root
[CLAUDE.md](../../../CLAUDE.md); this file is only what is specific to this module.

Sign-in is **ASP.NET Core Identity**, not hand-written. A session token is a sealed, data-protected
bearer token, not a signed JWT. Passwords, hashing, lockout and the token format all belong to the
framework, so there is nothing of ours to read for them.

## What it persists

Schema `identity`, connection string name `Identity`, database role `identity_svc`, context
`IdentityDbContext` (which derives from `IdentityUserContext<AppUser, Guid>`).

| Table | Comes from |
|---|---|
| `AspNetUsers`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens` | the base context |
| `user_preferences` | ours: theme and language, keyed on user id |

There is no roles table: `IdentityUserContext` is the no-roles base, and this app has no roles.

## What it publishes and consumes

Nothing either way. `StockPortfolio.Modules.Identity.Contracts` holds **no code on purpose** —
nothing calls Identity at run time, because a token is self-contained and checking one needs no call
here. `EmptyShells_AreExactlyThePhasesNotYetBuilt` in
`tests/StockPortfolio.Architecture.Tests/ModuleBoundaryTests.cs` names that assembly and goes red if
a type is added. Identity references no other module.

## Where the interesting code is

| File | Why you would open it |
|---|---|
| `StockPortfolio.Modules.Identity.Api/IdentityEndpoints.cs` | Every route: `/api/auth` register, login, refresh, logout, me, plus `/api/settings/appearance` (GET and PUT) |
| `StockPortfolio.Modules.Identity.Infrastructure/IdentityModule.cs` | The whole wire-up, including `AddIdentityCore` and the store |
| `StockPortfolio.Modules.Identity.Infrastructure/Persistence/IdentityDbContext.cs` | What is mapped, and the converters for theme and language |
| `StockPortfolio.Modules.Identity.Domain/UserPreferences.cs` | The one entity we own; `AppUser.cs` beside it is a one-line subclass |
| `StockPortfolio.Modules.Identity.Application/Preferences/Wire.cs` | The only place `light`/`dark`/`system` and `en`/`uk` are turned into enum values |
| `src/Host/Extensions/AuthenticationExtensions.cs` | Not in this module, but it decides the claim name, the password rules and the token lifetime |

## Gotchas

- **The framework's own auth routes are never mapped.** The host calls `AddIdentityApiEndpoints`
  purely to get the schemes and `SignInManager`; the routes above are ours. Do not also call
  `MapIdentityApi` — you would get a second, differently-behaved `/register`.
- **The user id claim is `sub`, and the host sets it**, through `ClaimsIdentity.UserIdClaimType`.
  Every endpoint here, and both other modules, read `sub`. Changing it is a host change.
- **In `RegisterAsync` the status code and `Location` header are set before `SignInAsync`.**
  `SignInAsync` writes the whole response, so anything set afterwards is lost.
- **Logout rolls the security stamp**, which is the only revocation the framework offers. It signs
  the user out on every device, and the access token already issued stays valid until it expires.
- **Refresh unprotects the token itself** through `BearerTokenOptions.RefreshTokenProtector`, checks
  expiry against `TimeProvider`, then re-validates the security stamp. There is no refresh-token
  table, no rotation and no grace window — do not go looking for them.
- **The tokens are sealed by the Data Protection key ring, which MarketData stores** in
  `marketdata.data_protection_keys`. Break that and every existing session stops working.
- `IdentityDbContext.OnModelCreating` must call `base.OnModelCreating` **first** — that call is what
  maps `AspNetUsers` and its three siblings.
- The context throws on `SkippedEntityTypeConfigurationWarning`, so an `IEntityTypeConfiguration`
  outside the `StockPortfolio.Modules.Identity` namespace fails the build of the model rather than
  being ignored.
