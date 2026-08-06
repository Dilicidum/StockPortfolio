# Phase 5 — Make it mine: implementation plan

> **For agentic workers:** use `superpowers:subagent-driven-development` or `superpowers:executing-plans` to
> work through this task by task. Steps are checkboxes.

**Goal:** ship the settings surface — theme, language, dashboard refresh interval, position visibility and a
per-user market-data API key — so that every item in `phase-5-make-it-mine.md`'s "Done when" list passes in a
browser and on the public URL.

**Architecture:** each module owns the settings it can enforce, and serves them on its own routes. Identity
gains a preferences row; Portfolio gains a dashboard-settings row and the missing write path for the
visibility flag it already reads; MarketData gains its first database table, holding each user's encrypted
provider key alongside the framework's encryption key ring. Encryption itself is a port MarketData declares
and the host implements, because `.Infrastructure` may not reach ASP.NET Core. The SPA gains one settings
route with independently-saving sections, a theme applied before first paint, and English/Ukrainian
translations.

**Tech stack:** .NET 10, EF Core 10, Npgsql, ASP.NET Core Data Protection (shared framework, no new package),
React 19, TanStack Query 5.101.4 / Router 1.170.18, Tailwind v4, i18next + react-i18next, Vitest + MSW.

---

## Global constraints

Every task inherits these. They come from `CLAUDE.md` and are not restated per task.

- **Money is `decimal` server-side, serialised as a string.** Nothing about money is computed in JavaScript.
  A figure the **server computes** goes out as a string; a value the **user typed** arrives as a plain JSON
  number, because `JsonNumberHandling.Strict` rejects a quoted one.
- **`.Infrastructure` never references ASP.NET Core. `.Api` never references EF Core or its own
  `.Infrastructure`.** `LayerReferenceTests.IsAspNetCore` matches any assembly name starting with
  `Microsoft.AspNetCore`, and `FindForbiddenReferencePath` walks the graph transitively.
- **Entities have exactly one constructor: private, all-args, assigning only.** No validation inside it — EF
  binds it by parameter name on every row of every `SELECT`. Validation lives in the static `Create`.
- **A `ComplexProperty` cannot be a constructor parameter** (efcore#31621). Omit it and let the factory
  assign it through the private setter.
- **Handlers return `OneOf<…>` directly**, mapped with `.Match`, every lambda parameter named.
- **Every endpoint returns `Task<IResult>` and declares every status it can emit.** Do not declare 415 unless
  a real request with a wrong `Content-Type` has been seen to produce it.
- **Requests live in `.Api/Requests`** with the validator beside them; the endpoint constructs the command
  with `new`.
- **Repositories save their own changes.** No unit of work.
- **A generated migration does not compile here.** `CA1861` under `TreatWarningsAsErrors` rejects the inline
  `new[] { … }` EF emits for a composite index — hoist it to a `private static readonly string[]`, copying the
  comment from `20260805005340_InitialPortfolio.cs`.
- **Every `DbContext` needs `MigrationsHistoryTable("__EFMigrationsHistory", "<schema>")`.**
  `HasDefaultSchema` does not move it.
- **`Maximum Pool Size=2`** on every connection string.
- **No third-party UI component library** in the SPA. Native `<select>`, `<input type="checkbox">`.
- **Never add `UseResponseCompression()`.** It kills the alert stream.
- Tests are named `Method_Scenario_Expectation`. `CA1707` is suppressed in `tests/Directory.Build.props`.
- `dotnet test` on this machine fails under Smart App Control; run it in a Linux SDK container.

---

## Decisions taken before this plan

Recorded here because a later reader will otherwise re-open them.

| # | Decision | Why |
|---|---|---|
| D1 | **Dashboard totals follow visibility.** Hidden positions are excluded from the headline figures. | Already true: `HoldingQueries.GetVisibleHoldingsAsync` filters `IsVisible` in SQL, so hidden rows never reach `DashboardCalculator`, and `Weight` divides by that same visible total — percentages still sum to 100. Zero backend work. The README explains it. The "visible headline plus a quiet including-hidden line" option was considered and cut as complication the brief did not ask for. |
| D2 | **MarketData owns the per-user key and validates it. The caller's id is threaded through the price contract.** | Storing it in Identity would make MarketData call Identity on every dashboard load — the first runtime dependency on the one module deliberately kept extractable — and would force Identity to learn what a provider key looks like. Threading the id follows the pattern every module already uses; the alternatives (an ambient scoped holder, `IHttpContextAccessor`) both fail silently back to the shared key when a path forgets to set them. |
| D3 | **A user supplying their own key gets its own outbound path: no token bucket, and its own circuit breaker.** | The bucket is sized for one shared key, so a user who brought their own quota must not spend the application's. Bypassing the bucket alone is not enough, and this is the half the first draft got wrong: every request would still traverse the one resilience handler, so a user whose key is revoked generates 401s on the **shared** breaker — ten in thirty seconds opens it and blanks every other user's dashboard and starves the poller. Bring-your-own-key therefore gets a second named `HttpClient` with the same policy and a separate breaker. No per-key bucket: the only thing it would protect is that user's own Finnhub quota, and the provider's 429 already tells them. |
| D4 | **Both the user-key table and the encryption key ring live in the `marketdata` schema, in `MarketDataDbContext`.** | The role, schema and grants already exist unused since Phase 1; `db/init/*` needs no change. One context, one migration, one history table, no new password through compose / `.env.example` / Bicep / two workflows. |
| D5 | **The alert threshold keeps its existing route.** No `PATCH /api/settings/alerts` is added. | `PUT /api/alerts/settings` already ships, already upserts threshold + window + enabled, and already has a validator, a 409 for a window exceeding retention, and tests. The plan file's proposed route would be a second way to do one thing. The cosmetic inconsistency (`/api/alerts/settings` beside `/api/settings/*`) is not worth breaking a shipped route for. |
| D6 | **There is no aggregate `GET /api/settings`.** Each module serves its own section and the settings screen fetches them in parallel. | An aggregate would have to live in the host and call three modules' handlers, which makes the host the place every feature has to touch — the exact thing `StockPortfolio.Modules.<M>.Api` exists to prevent. Five parallel `GET`s on one screen cost nothing the user can perceive: appearance, dashboard settings, alert settings, key status, **and the holdings list the visibility section needs**. |
| D7 | **Encryption is a port MarketData declares and the host implements.** | `Microsoft.AspNetCore.DataProtection.Abstractions` and `…EntityFrameworkCore` both trip rule 4. The host may reference ASP.NET Core freely and already carries two adapters of exactly this shape. |

### The route surface after this phase

```
GET    /api/settings/appearance        Identity     theme, language
PUT    /api/settings/appearance        Identity
GET    /api/settings/dashboard         Portfolio    refreshIntervalSeconds
PUT    /api/settings/dashboard         Portfolio
PATCH  /api/holdings/{id}/visibility   Portfolio    show or hide one position
GET    /api/alerts/settings            Alerts       already ships, unchanged
PUT    /api/alerts/settings            Alerts       already ships, unchanged
GET    /api/settings/api-key           MarketData   configured?, last four
POST   /api/settings/api-key           MarketData   validate live, then store
DELETE /api/settings/api-key           MarketData
```

`PUT` rather than `PATCH` on appearance and dashboard: both sections send every field they own, so this is a
replace, not a partial update. It also matches `PUT /api/alerts/settings`, which is the same shape.

---

## File structure

**Identity** — `src/Modules/Identity/…`

| File | Responsibility |
|---|---|
| `.Domain/UserPreferences.cs` | theme + language + `UserId`; private all-args ctor; `CreateDefault`, `ChangeAppearance` |
| `.Domain/ThemeChoice.cs`, `.Domain/LanguageChoice.cs` | two enums: `Light, Dark, System` / `English, Ukrainian` |
| `.Application/Abstractions/IUserPreferencesRepository.cs` | `FindAsync`, `SaveAsync` (commits) |
| `.Application/Preferences/Queries/GetAppearance/…` | query, handler, `GetAppearanceResult` |
| `.Application/Preferences/Commands/SaveAppearance/…` | command, handler, `SaveAppearanceResult` |
| `.Infrastructure/Persistence/Configurations/UserPreferencesConfiguration.cs` | table `user_preferences`, enums as text |
| `.Infrastructure/Persistence/UserPreferencesRepository.cs` | EF implementation |
| `.Infrastructure/Persistence/Migrations/*_AddUserPreferences.cs` | generated, then hand-checked for CA1861 |
| `.Api/Requests/SaveAppearanceRequest.cs`, `.Api/Validators/SaveAppearanceRequestValidator.cs` | wire shape |

**Portfolio** — `src/Modules/Portfolio/…`

| File | Responsibility |
|---|---|
| `.Domain/DashboardSettings.cs` | `UserId` + refresh interval; `CreateDefault`, `ChangeInterval` |
| `.Domain/RefreshInterval.cs` | value object: seconds, 10–300, default 60 |
| `.Domain/Holding.cs` (modify) | add `SetVisibility(bool)` |
| `.Application/Abstractions/IDashboardSettingsRepository.cs` | `FindAsync`, `SaveAsync` (commits) |
| `.Application/Dashboard/Queries/GetDashboardSettings/…` | query, handler, result |
| `.Application/Dashboard/Commands/SaveDashboardSettings/…` | command, handler, result |
| `.Application/Holdings/Commands/SetHoldingVisibility/…` | command, handler, result |
| `.Infrastructure/Persistence/Configurations/DashboardSettingsConfiguration.cs` | table `dashboard_settings` |
| `.Infrastructure/Persistence/DashboardSettingsRepository.cs` | EF implementation |
| `.Api/Requests/…`, `.Api/Validators/…` | two requests, two validators |

**MarketData** — `src/Modules/MarketData/…`

| File | Responsibility |
|---|---|
| `.Infrastructure/Persistence/MarketDataDbContext.cs` | schema `marketdata`; two `DbSet`s |
| `.Domain/UserProviderKey.cs` | user id, ciphertext, last four, saved-at |
| `.Domain/KeyRingEntry.cs` | friendly name + XML blob; the framework's key ring, stored as data |
| `.Application/Abstractions/ISecretProtector.cs` | `Protect(string) : string`, `Unprotect(string) : string?` — **no ASP.NET types** |
| `.Application/Abstractions/IKeyRingStore.cs` | `GetAll() : IReadOnlyList<string>`, `Store(string, string)` — synchronous, called rarely |
| `.Application/Abstractions/IUserProviderKeyRepository.cs` | `FindAsync`, `SaveAsync`, `DeleteAsync` |
| `.Application/Abstractions/IUserProviderKeyReader.cs` | `ReadPlaintextAsync(Guid, ct) : string?` |
| `.Application/Keys/Commands/SaveApiKey/…` | validate live, protect, store |
| `.Application/Keys/Commands/RemoveApiKey/…` | delete |
| `.Application/Keys/Queries/GetApiKeyStatus/…` | configured?, last four |
| `.Infrastructure/Persistence/*Repository.cs`, `*Configuration.cs`, `Migrations/*_InitialMarketData.cs` | persistence |
| `.Contracts/IQuoteReader.cs` (modify) | gains the caller's id |
| `.Api/MarketDataEndpoints.cs` (modify) | three key routes |

**Host** — `src/Api/…`

| File | Responsibility |
|---|---|
| `Adapters/DataProtectionSecretProtector.cs` | implements `ISecretProtector` over `IDataProtector` |
| `Adapters/KeyRingXmlRepository.cs` | implements `IXmlRepository` over `IKeyRingStore` |
| `Extensions/DataProtectionExtensions.cs` | `AddStockPortfolioDataProtection`, plus the startup check that the protector is registered |

**SPA** — `src/Web/…`

| File | Responsibility |
|---|---|
| `index.html` (modify) | the blocking inline theme script |
| `src/index.css` (modify) | `@custom-variant dark` moves to `[data-theme="dark"]` |
| `src/lib/theme.ts` | read/write the choice, watch the OS preference, apply to the document |
| `src/lib/i18n.ts` | i18next setup; server wins over the cached language once signed in |
| `src/locales/en/*.json`, `src/locales/uk/*.json` | one file per feature area |
| `src/settings/settingsApi.ts` | types, `settingsKeys`, fetchers |
| `src/settings/*.tsx` | one component per section |
| `src/routes/_authenticated/settings.tsx` | the route |
| `scripts/check-locale-parity.mjs` | build check that both languages have the same keys |
| `tests/settings.test.tsx`, `tests/theme.test.tsx`, `tests/msw/settings.ts` | coverage |

---

## Task 1 — Identity: theme and language

**Files:**
- Create: `src/Modules/Identity/StockPortfolio.Modules.Identity.Domain/ThemeChoice.cs`, `LanguageChoice.cs`, `UserPreferences.cs`
- Create: `…Identity.Application/Abstractions/IUserPreferencesRepository.cs`
- Create: `…Identity.Application/Preferences/Queries/GetAppearance/{GetAppearanceQuery,GetAppearanceQueryHandler,GetAppearanceResult}.cs`
- Create: `…Identity.Application/Preferences/Commands/SaveAppearance/{SaveAppearanceCommand,SaveAppearanceCommandHandler}.cs`
- Create: `…Identity.Infrastructure/Persistence/Configurations/UserPreferencesConfiguration.cs`, `…/Persistence/UserPreferencesRepository.cs`
- Create: `…Identity.Api/Requests/SaveAppearanceRequest.cs`, `…Identity.Api/Validators/SaveAppearanceRequestValidator.cs`
- Modify: `…Identity.Infrastructure/Persistence/IdentityDbContext.cs` (one `DbSet`), `…Identity.Infrastructure/IdentityModule.cs` (repository), `…Identity.Infrastructure/DependencyInjection.cs` (two handlers), `…Identity.Api/IdentityEndpoints.cs` (two routes)
- Test: `tests/StockPortfolio.Modules.Identity.UnitTests/UserPreferencesTests.cs`, `…/SaveAppearanceRequestValidatorTests.cs`, `…/EfConstructorBindingTests.cs` (extend), `tests/StockPortfolio.Api.IntegrationTests/AppearanceSettingsTests.cs`, `…/EndpointMetadataTests.cs` (extend)

Identity has **one test file per validator** — `LoginUserRequestValidatorTests.cs`,
`RegisterUserRequestValidatorTests.cs`, `RefreshSessionRequestValidatorTests.cs`. Follow that, not
Portfolio's single pooled `RequestValidatorTests.cs`.

**Interfaces produced:**
- `GetAppearanceResult(string Theme, string Language)` — the enum names lower-cased on the wire: `"light" | "dark" | "system"`, `"en" | "uk"`.
- `IUserPreferencesRepository.FindAsync(UserId, CancellationToken) : Task<UserPreferences?>` and `SaveAsync(UserPreferences, CancellationToken) : Task` — `SaveAsync` commits.

- [ ] **Step 1: write the failing domain test**

`tests/StockPortfolio.Modules.Identity.UnitTests/UserPreferencesTests.cs`:

```csharp
public sealed class UserPreferencesTests
{
    [Fact]
    public void CreateDefault_ForAUser_IsSystemThemeAndEnglish()
    {
        var preferences = UserPreferences.CreateDefault(UserId.New());

        preferences.Theme.ShouldBe(ThemeChoice.System);
        preferences.Language.ShouldBe(LanguageChoice.English);
    }

    [Fact]
    public void ChangeAppearance_WithBothValues_ReplacesBoth()
    {
        var preferences = UserPreferences.CreateDefault(UserId.New());

        preferences.ChangeAppearance(ThemeChoice.Dark, LanguageChoice.Ukrainian);

        preferences.Theme.ShouldBe(ThemeChoice.Dark);
        preferences.Language.ShouldBe(LanguageChoice.Ukrainian);
    }
}
```

- [ ] **Step 2: run it and watch it fail**

```bash
dotnet test tests/StockPortfolio.Modules.Identity.UnitTests --filter UserPreferencesTests
```

Expected: does not compile — `UserPreferences` does not exist.

- [ ] **Step 3: write the domain**

`ThemeChoice.cs` and `LanguageChoice.cs` are plain enums in
`StockPortfolio.Modules.Identity.Domain`:

```csharp
public enum ThemeChoice { Light, Dark, System }
public enum LanguageChoice { English, Ukrainian }
```

`UserPreferences.cs`:

```csharp
namespace StockPortfolio.Modules.Identity.Domain;

public sealed class UserPreferences
{
    // EF binds this by parameter name on every row it loads, so it assigns and does nothing else.
    private UserPreferences(UserId userId, ThemeChoice theme, LanguageChoice language)
    {
        UserId = userId;
        Theme = theme;
        Language = language;
    }

    public UserId UserId { get; private set; }

    public ThemeChoice Theme { get; private set; }

    public LanguageChoice Language { get; private set; }

    /// <summary>The row a user gets the first time anything reads their preferences.</summary>
    public static UserPreferences CreateDefault(UserId userId) =>
        new(userId, ThemeChoice.System, LanguageChoice.English);

    public void ChangeAppearance(ThemeChoice theme, LanguageChoice language)
    {
        Theme = theme;
        Language = language;
    }
}
```

There is no `Create` returning a `OneOf` here: both values are enums, so the only invalid input is one that
never parses, and that is rejected at the edge by the validator in step 9.

- [ ] **Step 4: run the test and watch it pass**

- [ ] **Step 5: map it**

`UserPreferencesConfiguration.cs` in `…Infrastructure/Persistence/Configurations/`:

```csharp
internal sealed class UserPreferencesConfiguration : IEntityTypeConfiguration<UserPreferences>
{
    public void Configure(EntityTypeBuilder<UserPreferences> builder)
    {
        builder.ToTable("user_preferences");

        builder.HasKey(p => p.UserId);
        builder.Property(p => p.UserId).HasColumnName("user_id").ValueGeneratedNever();

        builder.Property(p => p.Theme).HasColumnName("theme").HasMaxLength(16).IsRequired();
        builder.Property(p => p.Language).HasColumnName("language").HasMaxLength(16).IsRequired();

        builder.HasOne<User>()
            .WithOne()
            .HasForeignKey<UserPreferences>(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

Add to `IdentityDbContext`: `public DbSet<UserPreferences> UserPreferences => Set<UserPreferences>();`

Both enums are stored as **text**, not as an int: a later migration that reorders the enum then cannot
silently repaint every row. But do **not** reach for a per-property `.HasConversion<string>()`. Follow
`AlertsDbContext:57-58`, which registers `AlertDirectionConverter` (an `EnumToStringConverter<T>`) in
`ConfigureConventions` as **both** `Properties<T>()` **and** `DefaultTypeMapping<T>()`. That is the rule in
`CLAUDE.md`'s "Where Identity is not a safe template" table, and Task 2 quotes it verbatim for
`RefreshInterval` — doing the opposite here would leave two conventions in one repo. Per-property conversion
works at model build and then throws at runtime when one of these enums appears in a LINQ closure, which is
exactly what `DefaultTypeMapping` exists to prevent.

So: `ThemeChoiceConverter` and `LanguageChoiceConverter` in `…/Persistence/Converters/`, both registered
twice, beside the existing `UserIdConverter` and `RefreshTokenIdConverter`. `UserId` itself needs nothing new.

- [ ] **Step 6: extend the constructor-binding test**

`EfConstructorBindingTests.cs` walks every entity in the model. Add `UserPreferences` to whatever theory
data drives it, then run the test — it is the thing that catches a parameter renamed on one side only, which
fails at **model build**, i.e. host startup, not on the first query.

- [ ] **Step 7: generate the migration**

```bash
dotnet ef migrations add AddUserPreferences --context IdentityDbContext --output-dir Persistence/Migrations --project src/Modules/Identity/StockPortfolio.Modules.Identity.Infrastructure --startup-project src/Api
```

Then open the generated file and check for an inline `new[] { … }`. There should be none — this table has a
single-column key and no composite index — but check, because `CA1861` under `TreatWarningsAsErrors` turns
one into a build error. Run `dotnet build` before committing.

- [ ] **Step 8: the repository**

`IUserPreferencesRepository.cs` in `…Application/Abstractions/`:

```csharp
public interface IUserPreferencesRepository
{
    Task<UserPreferences?> FindAsync(UserId userId, CancellationToken ct);

    /// <summary>Inserts or updates the row and <b>commits</b>.</summary>
    Task SaveAsync(UserPreferences preferences, CancellationToken ct);
}
```

`UserPreferencesRepository.cs` in `…Infrastructure/Persistence/`:

```csharp
internal sealed class UserPreferencesRepository(IdentityDbContext context) : IUserPreferencesRepository
{
    public Task<UserPreferences?> FindAsync(UserId userId, CancellationToken ct) =>
        context.UserPreferences.FirstOrDefaultAsync(p => p.UserId == userId, ct);

    public async Task SaveAsync(UserPreferences preferences, CancellationToken ct)
    {
        if (context.Entry(preferences).State == EntityState.Detached)
        {
            context.UserPreferences.Add(preferences);
        }

        await context.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 9: the two use cases**

`GetAppearanceQuery.cs`: `public sealed record GetAppearanceQuery(Guid UserId);`

`GetAppearanceResult.cs`: `public sealed record GetAppearanceResult(string Theme, string Language);`

`GetAppearanceQueryHandler.cs` — note it **creates the row on first read**, which is what keeps registration
a single insert:

```csharp
public sealed class GetAppearanceQueryHandler(IUserPreferencesRepository repository)
    : IQueryHandler<GetAppearanceQuery, GetAppearanceResult>
{
    public async Task<GetAppearanceResult> Handle(GetAppearanceQuery query, CancellationToken ct)
    {
        var userId = new UserId(query.UserId);
        var preferences = await repository.FindAsync(userId, ct) ?? UserPreferences.CreateDefault(userId);

        return new GetAppearanceResult(Wire.Theme(preferences.Theme), Wire.Language(preferences.Language));
    }
}
```

It does **not** persist the default. A read is a read; the row lands on the first write. Two concurrent
first-reads therefore cannot race on an insert.

`Wire` is a small internal static in the same namespace mapping the enums to and from their wire spellings —
`ThemeChoice.System` ⇄ `"system"`, `LanguageChoice.Ukrainian` ⇄ `"uk"`. Put it beside the command, not in a
shared file: two places need it and both are in this feature area.

`SaveAppearanceCommand.cs`: `public sealed record SaveAppearanceCommand(Guid UserId, string Theme, string Language);`

`SaveAppearanceCommandHandler.cs` returns `OneOf<GetAppearanceResult, InvalidInput>` — reusing the read's
result record, because the successful response to a save is the saved state and inventing a second identical
record would be duplication:

```csharp
public sealed class SaveAppearanceCommandHandler(IUserPreferencesRepository repository)
    : ICommandHandler<SaveAppearanceCommand, OneOf<GetAppearanceResult, InvalidInput>>
{
    public async Task<OneOf<GetAppearanceResult, InvalidInput>> Handle(
        SaveAppearanceCommand command, CancellationToken ct)
    {
        if (!Wire.TryParseTheme(command.Theme, out var theme))
        {
            return new InvalidInput("theme", "Theme must be light, dark or system.");
        }

        if (!Wire.TryParseLanguage(command.Language, out var language))
        {
            return new InvalidInput("language", "Language must be en or uk.");
        }

        var userId = new UserId(command.UserId);
        var preferences = await repository.FindAsync(userId, ct) ?? UserPreferences.CreateDefault(userId);
        preferences.ChangeAppearance(theme, language);
        await repository.SaveAsync(preferences, ct);

        return new GetAppearanceResult(Wire.Theme(theme), Wire.Language(language));
    }
}
```

The `InvalidInput` arm is reachable only if the validator and the handler disagree about the allowed set,
which is exactly the drift worth a result case rather than a throw.

**Two conventions to match rather than invent.** `InvalidInput` is `record InvalidInput(string Field, string
Message)` and every existing producer passes an **English sentence**, not a translation key — see
`Holding.cs:170`. Server-side messages are not translated in this phase; only the SPA's own zod messages are
(Task 9), and only because they already used keys. And the field name is **lower-case** everywhere in the
repo (`"quantity"`, `"price"`, `"ticker"`), so write `"theme"`, not `nameof(command.Theme)`.

- [ ] **Step 10: request, validator, endpoints**

`SaveAppearanceRequest.cs`: `public sealed record SaveAppearanceRequest(string Theme, string Language);`

`SaveAppearanceRequestValidator.cs`:

```csharp
public sealed class SaveAppearanceRequestValidator : AbstractValidator<SaveAppearanceRequest>
{
    public static readonly string[] Themes = ["light", "dark", "system"];
    public static readonly string[] Languages = ["en", "uk"];

    public SaveAppearanceRequestValidator()
    {
        RuleFor(r => r.Theme).Cascade(CascadeMode.Stop)
            .NotEmpty().WithErrorCode("theme.required")
            .Must(Themes.Contains!).WithErrorCode("theme.unknown");

        RuleFor(r => r.Language).Cascade(CascadeMode.Stop)
            .NotEmpty().WithErrorCode("language.required")
            .Must(Languages.Contains!).WithErrorCode("language.unknown");
    }
}
```

Assert on `ErrorCode` in the validator tests, never on message wording — `B10` in `deferred-work.md` records
that the existing validator tests got this wrong.

In `IdentityEndpoints.cs`, add to the existing `/api/auth` group? **No** — add a second group:

```csharp
var settings = app.MapGroup("/api/settings")
    .WithTags("Settings")
    .RequireAuthorization()
    .ProducesProblem(StatusCodes.Status401Unauthorized)
    .ProducesProblem(StatusCodes.Status500InternalServerError);

settings.MapGet("/appearance", GetAppearanceAsync)
    .WithName("GetAppearance")
    .Produces<GetAppearanceResult>(StatusCodes.Status200OK);

settings.MapPut("/appearance", SaveAppearanceAsync)
    .AddEndpointFilter<ValidationFilter<SaveAppearanceRequest>>()
    .WithName("SaveAppearance")
    .Produces<GetAppearanceResult>(StatusCodes.Status200OK)
    .ProducesValidationProblem();
```

Read the user id exactly as `GetCurrentUserAsync` does — `principal.FindFirstValue(SubjectClaimType)` then
`Guid.TryParse`, 401 on failure.

**Do not add `.ProducesProblem(415)` yet.** Drive a real request with a wrong `Content-Type` in step 12 and
add the declaration only if a 415 actually comes back. A missing body is a 400, not a 415, and `POST
/api/holdings` has carried an undemonstrated 415 declaration since Phase 2.

- [ ] **Step 11: register**

In `IdentityModule.AddIdentityModule`: `services.AddScoped<IUserPreferencesRepository, UserPreferencesRepository>();`

In `DependencyInjection.AddIdentityHandlers`, the two closed generics, spelled in full:

```csharp
services.AddScoped<IQueryHandler<GetAppearanceQuery, GetAppearanceResult>, GetAppearanceQueryHandler>();
services.AddScoped<
    ICommandHandler<SaveAppearanceCommand, OneOf<GetAppearanceResult, InvalidInput>>,
    SaveAppearanceCommandHandler>();
```

- [ ] **Step 12: integration tests**

`tests/StockPortfolio.Api.IntegrationTests/AppearanceSettingsTests.cs`, in the existing collection, using
`Wire.RegisterAsync`:

- `Get_ForANewUser_ReturnsSystemAndEnglish`
- `Put_ThenGet_ReturnsWhatWasSaved` — put `dark`/`uk`, read it back
- `Put_WithAnUnknownTheme_Returns400`
- `Get_Anonymous_Is401`
- `Put_WithWrongContentType_ReturnsWhateverItReallyReturns` — assert the status you observe, then make
  the endpoint's `.Produces` list say the same thing

Add `"GetAppearance"` and `"SaveAppearance"` to `EndpointMetadataTests`' `ExpectedRouteNames["Identity"]`.
That dictionary drives `ShouldExposeExactly`, so the test fails correctly without it — but the test is
**named** `EndpointDataSource_ExposesTheFiveAuthRoutes` and its doc comment says "the five names". Both
become false at seven. Rename it and fix the comment; in a repo whose stated position is that a test which
lies is itself the bug, leaving that is not an option.

Three counts that do **not** change, stated so nobody goes looking: `EndpointMetadataTests`'
`modules.Count.ShouldBe(4)` (no new `.Api` assembly ships), `ModuleBoundaryTests`' 22 assemblies (no new
project), and `EmptyShells_AreExactlyThePhasesNotYetBuilt` (nothing is added to `Identity.Contracts`, so it
stays empty and the two architecture skips stay at two).

- [ ] **Step 13: build, test, commit**

```bash
dotnet build && dotnet test
```

```bash
git add -A && git commit -m "Identity remembers a theme and a language"
```

---

## Task 2 — Portfolio: the dashboard refresh interval

**Files:**
- Create: `…Portfolio.Domain/RefreshInterval.cs`, `…Portfolio.Domain/DashboardSettings.cs`
- Create: `…Portfolio.Application/Abstractions/IDashboardSettingsRepository.cs`
- Create: `…Portfolio.Application/Dashboard/Queries/GetDashboardSettings/{Query,Handler,Result}.cs`
- Create: `…Portfolio.Application/Dashboard/Commands/SaveDashboardSettings/{Command,Handler}.cs`
- Create: `…Portfolio.Infrastructure/Persistence/Configurations/DashboardSettingsConfiguration.cs`, `…/DashboardSettingsRepository.cs`, `…/Converters/RefreshIntervalConverter.cs`
- Create: `…Portfolio.Api/Requests/SaveDashboardSettingsRequest.cs`, `…Portfolio.Api/Validators/SaveDashboardSettingsRequestValidator.cs`
- Modify: `PortfolioDbContext.cs`, `PortfolioModule.cs`, Portfolio's `DependencyInjection.cs`, `PortfolioEndpoints.cs`
- Test: `tests/StockPortfolio.Modules.Portfolio.UnitTests/RefreshIntervalTests.cs`, `…/DashboardSettingsTests.cs`, `…/RequestValidatorTests.cs` (extend), `…/EfModelTests.cs` (extend), `tests/StockPortfolio.Api.IntegrationTests/DashboardSettingsTests.cs`

**Interfaces produced:**
- `GetDashboardSettingsResult(int RefreshIntervalSeconds)` — a plain JSON number both ways. It is not money
  and the user types it.
- `RefreshInterval` — `readonly record struct RefreshInterval` with `int Seconds`, `Minimum = 10`,
  `Maximum = 300`, `Default => new(60)`, and `static OneOf<RefreshInterval, InvalidInput> Create(int)`.

- [ ] **Step 1: the failing value-object test**

```csharp
public sealed class RefreshIntervalTests
{
    [Theory]
    [InlineData(10)]
    [InlineData(60)]
    [InlineData(300)]
    public void Create_InRange_Succeeds(int seconds) =>
        RefreshInterval.Create(seconds).AsT0.Seconds.ShouldBe(seconds);

    [Theory]
    [InlineData(9)]
    [InlineData(301)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_OutOfRange_IsInvalidInput(int seconds) =>
        RefreshInterval.Create(seconds).IsT1.ShouldBeTrue();

    [Fact]
    public void Default_IsSixtySeconds() => RefreshInterval.Default.Seconds.ShouldBe(60);
}
```

The last assertion is load-bearing and must not be "corrected" downward: 60 seconds is the number the
capacity claim in the Phase 3 plan and the README paragraph built on it both rest on.

- [ ] **Step 2: run it, watch it fail. Step 3: write `RefreshInterval` and `DashboardSettings`**

`DashboardSettings` mirrors `UserPreferences`: private all-args constructor taking `(Guid userId,
RefreshInterval interval)`, `CreateDefault(Guid)`, `ChangeInterval(RefreshInterval)`. Portfolio stores the
user as a plain `Guid` — `UserId` lives in `Identity.Domain` and a module referencing a user it does not own
stores a raw `Guid`, exactly as `Holding` already does.

- [ ] **Step 4: run, pass. Step 5: map it**

`RefreshIntervalConverter : ValueConverter<RefreshInterval, int>` in `…/Persistence/Converters/`, registered
in `PortfolioDbContext.ConfigureConventions` beside the existing `HoldingId` and `Ticker` converters —
**both** `Properties<RefreshInterval>()` and `DefaultTypeMapping<RefreshInterval>()`. Missing the second is
the failure mode `CLAUDE.md` records under "Where Identity is not a safe template".

`DashboardSettingsConfiguration`: table `dashboard_settings`, key `user_id`, column
`refresh_interval_seconds int NOT NULL`. No foreign key — Portfolio does not own the users table and cannot
reference across a schema boundary it has no grant on.

- [ ] **Step 6: extend `EfModelTests`** so the new entity is covered by whatever the existing model test
  asserts about mapped members and constructor binding.

- [ ] **Step 7: migration**

```bash
dotnet ef migrations add AddDashboardSettings --context PortfolioDbContext --output-dir Persistence/Migrations --project src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Infrastructure --startup-project src/Api
```

Check the generated file for an inline array; hoist it if present, copying the CA1861 comment from
`20260805005340_InitialPortfolio.cs`. Build before committing.

- [ ] **Step 8: repository, handlers, request, validator, endpoints**

Same shapes as Task 1. The get handler creates the default in memory and does not persist it; the save
handler returns `OneOf<GetDashboardSettingsResult, InvalidInput>` and gets its `InvalidInput` from
`RefreshInterval.Create`, so the range lives in the value object and the validator repeats it only as a
fast rejection at the edge.

Routes, added to a `/api/settings` group in `PortfolioEndpoints.cs` shaped exactly like Identity's:

```csharp
settings.MapGet("/dashboard", GetDashboardSettingsAsync)
    .WithName("GetDashboardSettings")
    .Produces<GetDashboardSettingsResult>(StatusCodes.Status200OK);

settings.MapPut("/dashboard", SaveDashboardSettingsAsync)
    .AddEndpointFilter<ValidationFilter<SaveDashboardSettingsRequest>>()
    .WithName("SaveDashboardSettings")
    .Produces<GetDashboardSettingsResult>(StatusCodes.Status200OK)
    .ProducesValidationProblem();
```

Three modules end up mapping a group at `/api/settings` — Identity, Portfolio and, in Task 6, MarketData.
That is fine: ASP.NET composes route groups by path, and each route is still owned by exactly one module. It
is the inverse of the case E2 warns about, which was one module serving two prefixes.

- [ ] **Step 9: integration tests, `ExpectedRouteNames["Portfolio"]`, build, test, commit**

`EndpointDataSource_ExposesTheFivePortfolioRoutes` needs the same rename as Identity's — Tasks 2 and 3 take
Portfolio from five routes to seven.

`git commit -m "The dashboard refresh interval is a saved setting"`

---

## Task 3 — Portfolio: hide and show a position

The flag exists, defaults to visible, and the dashboard already filters on it. What is missing is any way to
change it: `Holding` has no method, `UpdateHoldingCommand` carries only quantity and price, and no route
exists.

**Files:**
- Modify: `…Portfolio.Domain/Holding.cs`
- Create: `…Portfolio.Application/Holdings/Commands/SetHoldingVisibility/{Command,Handler}.cs`
- Create: `…Portfolio.Api/Requests/SetHoldingVisibilityRequest.cs` (no validator — see Step 6)
- Modify: `PortfolioEndpoints.cs`, Portfolio's `DependencyInjection.cs`
- Test: `tests/StockPortfolio.Modules.Portfolio.UnitTests/HoldingTests.cs` (extend), `tests/StockPortfolio.Api.IntegrationTests/HoldingVisibilityTests.cs`

- [ ] **Step 1: the failing tests**

```csharp
[Fact]
public void SetVisibility_ToFalse_HidesTheHolding()
{
    var holding = AVisibleHolding();

    holding.SetVisibility(false);

    holding.IsVisible.ShouldBeFalse();
}

[Fact]
public void SetVisibility_ToFalse_LeavesQuantityAndAveragePriceAlone()
{
    var holding = AVisibleHolding();
    var quantity = holding.Quantity;
    var average = holding.AveragePrice;

    holding.SetVisibility(false);

    holding.Quantity.ShouldBe(quantity);
    holding.AveragePrice.ShouldBe(average);
}
```

`AVisibleHolding()` builds one through `Holding.Create` — reuse whatever helper `HoldingTests.cs` already has
rather than adding a second.

- [ ] **Step 2: run, fail. Step 3: add the method**

```csharp
/// <summary>Hiding is a display filter: it changes no figure and no alert.</summary>
public void SetVisibility(bool isVisible)
{
    IsVisible = isVisible;
    UpdatedAt = ...;   // match exactly what Correct() does with the clock; do not invent a second pattern
}
```

Read `Correct` before writing this. If it takes a `TimeProvider`, so does this; if it stamps from a passed
`DateTimeOffset`, so does this.

- [ ] **Step 4: run, pass. Step 5: the command**

`SetHoldingVisibilityCommand(Guid UserId, Guid HoldingId, bool IsVisible)`, handler returning
`OneOf<Success, NotFound>` using `OneOf.Types.Success` and `OneOf.Types.NotFound` — both ship with the
package; do not redeclare them. Look the holding up scoped by `(userId, holdingId)` together, exactly as
`UpdateHoldingCommandHandler` does, so someone else's id is a 404 rather than a 403.

- [ ] **Step 6: the route**

```csharp
group.MapPatch("/{id:guid}/visibility", SetVisibilityAsync)
    .WithName("SetHoldingVisibility")
    .Produces(StatusCodes.Status204NoContent)
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status404NotFound);
```

`PATCH` here and `PUT` on the settings sections is deliberate and not an inconsistency: this changes one
field of a larger resource, the settings routes replace the whole of a small one.

The request is `public sealed record SetHoldingVisibilityRequest(bool IsVisible);`.

**Do not attach `ValidationFilter<SetHoldingVisibilityRequest>` and do not write an empty validator.** A
`bool` has no invalid value, so there is nothing to check — and the reason an empty one might look harmless
is wrong: `ValidationFilter<T>` calls `next(context)` when it finds no argument of type `T`
(`ValidationFilter.cs:15-18`), so it does **not** produce the 400 for a missing body. That 400 comes from
minimal-API body binding, before the filter runs. This is `deferred-work.md` A5, and an empty validator here
would enshrine the opposite belief in a comment nobody later dares delete.

Drop the `.AddEndpointFilter` line from the route below and the two request-validator files from this task's
file list. A5's status paragraph counts eight filtered routes; leave it at eight.

- [ ] **Step 7: integration tests**

`HoldingVisibilityTests.cs`:

- `Patch_ToHidden_RemovesTheRowFromTheDashboard` — add two holdings, hide one, read `/api/dashboard`,
  assert one position comes back
- `Patch_ToHidden_LeavesItOnTheHoldingsList` — `/api/holdings` still returns both, with `isVisible: false`
- `Patch_AHoldingOwnedBySomeoneElse_Returns404`
- `Patch_ToHidden_StillLetsAnAlertBeConfigured` — hide it, then `PUT /api/alerts/settings` for that ticker
  and assert 200. `IUserHoldsTicker` deliberately ignores visibility, and this is the test that stops
  someone "fixing" that.

That last one is the assertion this task exists for. The others would all stay green under an implementation
that also filtered hidden positions out of `HoldsAsync`, which would silently stop alerts firing on hidden
positions — the exact behaviour the phase plan says must not happen.

- [ ] **Step 8: register the handler, add `"SetHoldingVisibility"` to `ExpectedRouteNames["Portfolio"]`,
  build, test, commit**

`git commit -m "A position can be hidden from the dashboard without leaving the portfolio"`

---

## Task 4 — MarketData gets a database

The module stores nothing in Postgres today. This task creates the context, both tables and the migration,
and updates every test that pins how many contexts and schemas exist. It stops short of using either table —
that is Tasks 5 to 7 — so it can be reviewed on its own.

The `marketdata` schema, the `marketdata_svc` role and its grants already exist in `db/init/01-roles.sql`,
and `ConnectionStrings__MarketData` already exists in `docker-compose.yml`, `.env.example`,
`infra/main.bicep`, `infra/modules/containerapp-api.bicep` and `src/Migrator/Program.cs`. **None of those
files needs a change.** The only place a connection string is missing is the integration-test fixture.

**Files:**
- Create: `…MarketData.Domain/UserProviderKey.cs`, `…MarketData.Domain/KeyRingEntry.cs`
- Create: `…MarketData.Infrastructure/Persistence/MarketDataDbContext.cs`, `…/Configurations/{UserProviderKeyConfiguration,KeyRingEntryConfiguration}.cs`, `…/DesignTimeMarketDataDbContextFactory.cs`, `…/Migrations/*_InitialMarketData.cs`
- Modify: `…MarketData.Infrastructure/MarketDataModule.cs` (add `AddMarketDataPersistence`), `…MarketData.Infrastructure/StockPortfolio.Modules.MarketData.Infrastructure.csproj` (EF + Npgsql)
- Modify: `src/Migrator/MigratedModules.cs`
- Modify: `tests/StockPortfolio.Api.IntegrationTests/Infrastructure/ApiFixture.cs`, `…/Infrastructure/ModuleDbContextInterceptors.cs`, `…/MigrationTests.cs`, `…/SchemaIsolationTests.cs`

**Interfaces produced:**
- `MarketDataModule.ConnectionStringName = "MarketData"`, `MarketDataDbContext.SchemaName = "marketdata"`,
  `MarketDataDbContext.MigrationsHistoryTableName = "__EFMigrationsHistory"`.
- `AddMarketDataPersistence(this IServiceCollection, IConfiguration)`.

- [ ] **Step 1: write the failing migration assertion first**

In `MigrationTests.cs`, change the pinned list:

```csharp
historySchemas.ShouldBe(["alerts", "identity", "marketdata", "portfolio"]);
```

and add a `marketdata` block mirroring the existing per-schema table assertions.

- [ ] **Step 2: run it and watch it fail**

```bash
dotnet test tests/StockPortfolio.Api.IntegrationTests --filter MigrationTests
```

Expected: red, reporting three schemas where four were expected.

- [ ] **Step 3: the two entities**

`UserProviderKey.cs`:

```csharp
namespace StockPortfolio.Modules.MarketData.Domain;

public sealed class UserProviderKey
{
    private UserProviderKey(
        Guid userId, string ciphertext, string lastFour, DateTimeOffset savedAt, DateTimeOffset? lastRejectedAt)
    {
        UserId = userId;
        Ciphertext = ciphertext;
        LastFour = lastFour;
        SavedAt = savedAt;
        LastRejectedAt = lastRejectedAt;
    }

    public Guid UserId { get; private set; }

    /// <summary>The user's provider key, already protected. Never leaves the server.</summary>
    public string Ciphertext { get; private set; }

    public string LastFour { get; private set; }

    public DateTimeOffset SavedAt { get; private set; }

    /// <summary>Set when the provider refused this key on a real fetch, so the screen can say so.</summary>
    public DateTimeOffset? LastRejectedAt { get; private set; }

    public static UserProviderKey Create(Guid userId, string ciphertext, string lastFour, TimeProvider clock)
        => new(userId, ciphertext, lastFour, clock.GetUtcNow(), lastRejectedAt: null);

    public void Replace(string ciphertext, string lastFour, TimeProvider clock)
    {
        Ciphertext = ciphertext;
        LastFour = lastFour;
        SavedAt = clock.GetUtcNow();
        LastRejectedAt = null;
    }

    public void MarkRejected(TimeProvider clock) => LastRejectedAt = clock.GetUtcNow();
}
```

`LastRejectedAt` exists because a key validated once can be revoked later. Without it, a revoked key makes
every dashboard fall back to last-known prices with nothing anywhere saying why — `FinnhubQuoteProvider`
logs the 401 and returns null, and the user sees stale prices and no message. Task 7 sets it; Task 6's status
query reports it. The column is here rather than in a second migration because it is cheaper to add now than
to add later, and its absence is not discoverable from the happy path.

`KeyRingEntry.cs` is the framework's key ring stored as rows. It is deliberately dumb — an id, a friendly
name and an XML blob — because the framework owns the format and this module owns only the bytes:

```csharp
public sealed class KeyRingEntry
{
    private KeyRingEntry(Guid id, string friendlyName, string xml)
    {
        Id = id;
        FriendlyName = friendlyName;
        Xml = xml;
    }

    public Guid Id { get; private set; }

    public string FriendlyName { get; private set; }

    public string Xml { get; private set; }

    public static KeyRingEntry Create(string friendlyName, string xml) =>
        new(Guid.CreateVersion7(), friendlyName, xml);
}
```

- [ ] **Step 4: the context**

`MarketDataDbContext.cs`, copying `PortfolioDbContext`'s shape exactly — `internal sealed`, primary
constructor, `SchemaName`/`MigrationsHistoryTableName` constants, `HasDefaultSchema`,
`ApplyConfigurationsFromAssembly` with the namespace predicate, and the
`SkippedEntityTypeConfigurationWarning` throw. Two `DbSet`s: `UserProviderKeys`, `KeyRingEntries`. Tables
`user_provider_keys` (key `user_id`) and `data_protection_keys`.

Add the EF and Npgsql package references to the Infrastructure csproj. **Do not add any
`Microsoft.AspNetCore.DataProtection.*` package** — that is rule 4, and Task 5 explains what replaces it.

- [ ] **Step 5: `AddMarketDataPersistence`**

In `MarketDataModule.cs`, split out a persistence half the way `AddIdentityPersistence` is split out, and
call it from `AddMarketDataModule`:

```csharp
public const string ConnectionStringName = "MarketData";

public static IServiceCollection AddMarketDataPersistence(
    this IServiceCollection services, IConfiguration configuration)
{
    var connectionString = configuration.GetConnectionString(ConnectionStringName);
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException(
            $"Connection string '{ConnectionStringName}' is missing.");
    }

    // AddDbContext, never AddDbContextFactory: the Migrator finds contexts by service type.
    services.AddDbContext<MarketDataDbContext>(options => options.UseNpgsql(
        connectionString,
        npg => npg.MigrationsHistoryTable(
            MarketDataDbContext.MigrationsHistoryTableName,
            MarketDataDbContext.SchemaName)));

    return services;
}
```

Copy the exact wording of Identity's missing-connection-string message.

- [ ] **Step 6: design-time factory and migration**

Add `MarketDataDbContextFactory` mirroring `PortfolioDbContextFactory`, **including its
`MigrationsHistoryTable` call** (`PortfolioDbContextFactory.cs:25-26`) — omitting it there is how four
contexts end up sharing one history table with no error anywhere (efcore#24127; `deferred-work.md` C6).

The three existing factories are named `DesignTimeIdentityDbContextFactory` (inside `DesignTimeFactory.cs`),
`PortfolioDbContextFactory` and `AlertsDbContextFactory`. Two of three use the `<Context>Factory` form, so
use that and do not invent a fourth spelling.

```bash
dotnet ef migrations add InitialMarketData --context MarketDataDbContext --output-dir Persistence/Migrations --project src/Modules/MarketData/StockPortfolio.Modules.MarketData.Infrastructure --startup-project src/Api
```

Check for inline arrays and hoist. `dotnet build` must be clean before continuing.

- [ ] **Step 7: the migrator**

`MigratedModules.AddEveryMigratedModule` gains one line. The file is in **phase order**
(`AddIdentityPersistence`, `AddPortfolioModule`, `AddAlertsModule`) — not alphabetical — so append it:

```csharp
services.AddMarketDataPersistence(configuration);
```

The apply order does not follow this file anyway: `DbContextTypesIn` sorts by type name, so the migrator will
run `AlertsDbContext`, `IdentityDbContext`, `MarketDataDbContext`, `PortfolioDbContext`. Nothing depends on
that order — the four schemas are independent.

`src/Migrator/Program.cs` needs **no** change — it already fans `ConnectionStrings__Migrator` into a
`MarketData` override.

- [ ] **Step 8: the test fixture**

`ApiFixture.cs`: add a `MarketDataConnectionString` property mirroring `PortfolioConnectionString`, add
`["ConnectionStrings:MarketData"]` to both `SettingsFor(...)` and `MigratorConfiguration`, and add an
`AddToMarketData` to `ModuleDbContextInterceptors` wired in beside the other three so the
parameterisation interceptor watches this context too.

**`SettingsFor` is positional and has five call sites**, not one. It is declared at `ApiFixture.cs:246` as
`SettingsFor(identity, portfolio, alerts, redis)` and called at lines 141, 159, 179, 196 and 211 — including
`CreateHostWithRedisDown` at 179, which is easy to miss because it is about Redis. Adding a parameter is
seven edits: the declaration, five calls, and the new property. Consider taking a record instead of a fifth
positional argument, since a sixth is coming the next time a module persists something.

`SchemaIsolationTests.cs`: add `MarketDataRole_HasUsageOnMarketDataAlone` and
`MarketDataRole_CanReadItsOwnTables`, mirroring the Alerts pair, and extend
`AlertsRole_HasUsageOnAlertsAlone` to also assert no usage on `marketdata` — it currently checks three
schemas of four.

- [ ] **Step 9: run everything**

```bash
dotnet build && dotnet test
```

`MigrationTests` should now be green with four schemas. Then prove it from a clean volume, because the
one-command boot is the acceptance gate:

```bash
docker compose down -v && docker compose up
```

Expected: the migration job reports **four** contexts checked. Confirm the table exists:

```bash
docker compose exec postgres psql -U marketdata_svc -d stockportfolio -c "\dt marketdata.*"
```

- [ ] **Step 10: commit**

`git commit -m "MarketData gets its first tables, and the migrator its fourth context"`

---

## Task 5 — Encryption, without ASP.NET Core in Infrastructure

`ISecretProtector` and `IKeyRingStore` are ports MarketData declares in its own words. The host implements
both over the framework, which is what keeps `Microsoft.AspNetCore.DataProtection` out of
`.Infrastructure`. This is the same shape as `AlertsPollTargetSource` and `AlertsPriceSampleObserver`,
and it is the third adapter of that kind.

`Microsoft.AspNetCore.DataProtection` ships in the ASP.NET Core shared framework, so **no new package
reference is needed anywhere**. Only the EF and Redis key-store packages are separate, and neither is used.

**Files:**
- Create: `…MarketData.Application/Abstractions/ISecretProtector.cs`, `…/Abstractions/IKeyRingStore.cs`
- Create: `…MarketData.Infrastructure/Persistence/KeyRingStore.cs`
- Create: `src/Api/Adapters/DataProtectionSecretProtector.cs`, `src/Api/Adapters/KeyRingXmlRepository.cs`
- Create: `src/Api/Extensions/DataProtectionExtensions.cs`
- Modify: `src/Api/Program.cs`
- Test: `tests/StockPortfolio.Modules.MarketData.UnitTests/KeyRingStoreTests.cs`, `tests/StockPortfolio.Api.IntegrationTests/DataProtectionPersistenceTests.cs`, `tests/StockPortfolio.Architecture.Tests/LayerReferenceTests.cs` (verify, no change expected)

**Interfaces produced:**

```csharp
namespace StockPortfolio.Modules.MarketData.Application.Abstractions;

/// <summary>Scrambles a secret before it is stored. The host decides how.</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);

    /// <summary>Null when the stored value cannot be read back — a lost key ring, or tampering.</summary>
    string? Unprotect(string ciphertext);
}

/// <summary>Where the protector's own keys are kept. Synchronous: read once at startup, written on rotation.</summary>
public interface IKeyRingStore
{
    IReadOnlyList<string> GetAll();

    void Store(string friendlyName, string xml);
}
```

- [ ] **Step 1: the failing store test**

```csharp
[Fact]
public void Store_ThenGetAll_ReturnsWhatWasStored()
{
    using var store = AStoreOverAnInMemoryContext();

    store.Store("key-1", "<key id=\"1\" />");

    store.GetAll().ShouldBe(["<key id=\"1\" />"]);
}
```

Build the context over the EF in-memory-free path this repo already uses in `EfModelTests` — a real
`MarketDataDbContext` on `UseNpgsql` with a dummy connection string is enough for model assertions but not
for reads, so this specific test belongs in the integration project if a real database is needed. Decide
once and put it in one place; do not write it twice.

- [ ] **Step 2: run, fail. Step 3: the store**

`KeyRingStore.cs` in `…Infrastructure/Persistence/`, `internal sealed`, taking the context. `GetAll` selects
the `Xml` column; `Store` adds a `KeyRingEntry` and calls `SaveChanges`. Both synchronous, because
`IXmlRepository` is.

Because it is a singleton consumer of a scoped context, it takes `IServiceScopeFactory` and opens a scope per
call — the same rule the poller follows, and for the same reason.

- [ ] **Step 4: the two host adapters**

```csharp
// src/Api/Adapters/DataProtectionSecretProtector.cs
internal sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector protector = provider.CreateProtector("StockPortfolio.MarketData.UserProviderKey");

    public string Protect(string plaintext) => protector.Protect(plaintext);

    public string? Unprotect(string ciphertext)
    {
        try
        {
            return protector.Unprotect(ciphertext);
        }
        catch (CryptographicException)
        {
            // A rotated-away key ring, or a tampered row. Neither is recoverable and neither is an outage.
            return null;
        }
    }
}
```

```csharp
// src/Api/Adapters/KeyRingXmlRepository.cs
internal sealed class KeyRingXmlRepository(IKeyRingStore store) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements() =>
        [.. store.GetAll().Select(XElement.Parse)];

    public void StoreElement(XElement element, string friendlyName) =>
        store.Store(friendlyName, element.ToString(SaveOptions.DisableFormatting));
}
```

The purpose string passed to `CreateProtector` is part of the ciphertext. **Changing it makes every stored
key unreadable**, exactly as losing the ring would. Say so in a one-line comment above it.

- [ ] **Step 5: wire it up**

`DataProtectionExtensions.cs`:

`KeyRingStore` is `internal` to `MarketData.Infrastructure`, so the host cannot name the concrete type.
Register it from inside `AddMarketDataPersistence` (Task 4, Step 5), where it is visible:

```csharp
services.AddSingleton<IKeyRingStore, KeyRingStore>();
```

The host then consumes only the interface:

```csharp
public static IServiceCollection AddStockPortfolioDataProtection(this IServiceCollection services)
{
    services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();

    // Concrete, because the options callback below needs this exact type. IXmlRepository is never
    // registered as a service — the framework reads it off KeyManagementOptions, not from DI.
    services.AddSingleton<KeyRingXmlRepository>();

    services.AddDataProtection()
        .SetApplicationName("StockPortfolio")
        .Services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(sp =>
            new ConfigureOptions<KeyManagementOptions>(options =>
                options.XmlRepository = sp.GetRequiredService<KeyRingXmlRepository>()));

    return services;
}
```

`SetApplicationName` is not decoration: without it the application name is derived from the content root,
which differs between the container and a local `dotnet run`, and the derived value is mixed into the
ciphertext.

In `Program.cs`, call it **after** `AddMarketDataModule` (line 73), because the protector depends on
MarketData's key-ring store, and **before** `DecorateHandlers()`.

**The ring is not read at startup.** The framework resolves the key ring lazily, on the first
`Protect`/`Unprotect` — which is the first time someone saves or uses a key, long after the migration job
has finished. Do not add an eager warm-up: on a clean `docker compose down -v && docker compose up` the
`api` container can start before `marketdata.data_protection_keys` exists, and an eager read would turn that
race into a startup crash on the acceptance-gate path. `deferred-work.md` D10 records that compose's
ordering is already imperfect here. Write this reasoning into a comment above the call, because "add a
warm-up so the first request is fast" is an obvious-looking change that breaks the gate.

- [ ] **Step 6: the startup check**

Add to `DataProtectionExtensions` a `ValidateSecretProtectorIsRegistered(this IServiceCollection)` that
throws if `ISecretProtector` has no descriptor, and call it beside
`ValidateAlertWindowFitsRetention` in `Program.cs`. Without it, a missing registration is a failure on the
first key someone saves rather than at startup — and `TryAdd` cannot be used to give it a default, because a
module's `TryAdd` always wins the race to be first.

- [ ] **Step 7: prove the ring survives a restart**

`DataProtectionPersistenceTests.cs`: protect a value through a host, dispose it, build a second host against
the same database, and assert the second one unprotects it. This is the whole reason the ring is in Postgres —
if it passes with the ring in the container filesystem, the test is not testing anything, so break it
deliberately once (point the repository at the default file store) and watch it go red.

- [ ] **Step 8: confirm rule 4 still passes**

```bash
dotnet test tests/StockPortfolio.Architecture.Tests --filter InfrastructureAssembly_ReferencesNoAspNetCore
```

Expected: green, including `StockPortfolio.Modules.MarketData.Infrastructure`. If it is red, the DataProtection
reference has leaked into Infrastructure and the port is not doing its job.

- [ ] **Step 9: build, test, commit**

`git commit -m "The encryption key ring lives in Postgres, and Infrastructure never sees ASP.NET Core"`

---

## Task 6 — Bring your own key: store it

**Files:**
- Create: `…MarketData.Application/Abstractions/{IUserProviderKeyRepository,IUserProviderKeyReader}.cs`
- Create: `…MarketData.Application/Keys/Commands/SaveApiKey/{SaveApiKeyCommand,SaveApiKeyCommandHandler,SaveApiKeyResult,ProviderRejectedTheKey,ByokDisabled}.cs`
- Create: `…MarketData.Application/Keys/Commands/RemoveApiKey/{RemoveApiKeyCommand,RemoveApiKeyCommandHandler}.cs`
- Create: `…MarketData.Application/Keys/Queries/GetApiKeyStatus/{GetApiKeyStatusQuery,GetApiKeyStatusQueryHandler,GetApiKeyStatusResult}.cs`
- Create: `…MarketData.Infrastructure/Persistence/UserProviderKeyRepository.cs`, `…/Quotes/UserProviderKeyReader.cs`
- Create: `…MarketData.Api/Requests/SaveApiKeyRequest.cs`, `…Api/Validators/SaveApiKeyRequestValidator.cs`
- Modify: `MarketDataEndpoints.cs`, `MarketDataModule.cs`, `FinnhubOptions.cs` (the feature switch), `docker-compose.yml`, `.env.example`, `infra/main.bicep`, `infra/modules/containerapp-api.bicep`, `.github/workflows/deploy.yml`
- Test: `tests/StockPortfolio.Modules.MarketData.UnitTests/SaveApiKeyCommandHandlerTests.cs`, `tests/StockPortfolio.Api.IntegrationTests/ApiKeyTests.cs`

**Interfaces produced:**
- `GetApiKeyStatusResult(bool Configured, string? LastFour, bool Rejected)` — and nothing else. **The key is
  never returned, not even masked beyond the last four.** Every path that can return it is a path that can
  leak it. `Rejected` is `LastRejectedAt is not null`: it is what lets the settings screen say "your key was
  refused — re-enter it" instead of the user seeing stale prices and no explanation.
- `IUserProviderKeyReader.ReadPlaintextAsync(Guid userId, CancellationToken) : Task<string?>` — Task 7's input.

- [ ] **Step 1: the failing handler tests**

Build them on fakes, the way `tests/StockPortfolio.Modules.Alerts.UnitTests/Fakes/` does — that directory is
the only one of its kind in the repo and is the pattern to copy.

```csharp
[Fact]
public async Task Handle_WhenTheProviderRejectsTheKey_DoesNotStoreAnything()
{
    var repository = new FakeUserProviderKeyRepository();
    var handler = AHandler(repository, provider: RejectsEveryKey);

    var result = await handler.Handle(new SaveApiKeyCommand(AUser, "bad-key"), TestContext.Current.CancellationToken);

    result.IsT1.ShouldBeTrue();
    repository.Saved.ShouldBeEmpty();
}

[Fact]
public async Task Handle_WithAGoodKey_StoresCiphertextAndNeverThePlaintext()
{
    var repository = new FakeUserProviderKeyRepository();
    var handler = AHandler(repository, provider: AcceptsEveryKey);

    await handler.Handle(new SaveApiKeyCommand(AUser, "d1v3rs3-k3y-a1b2"), TestContext.Current.CancellationToken);

    var stored = repository.Saved.ShouldHaveSingleItem();
    stored.Ciphertext.ShouldNotContain("d1v3rs3-k3y-a1b2");
    stored.LastFour.ShouldBe("a1b2");
}
```

The second assertion is the one that matters. A protector fake that returns its input unchanged would make
the first test pass and this one fail, which is exactly the mistake worth catching — so write the fake
protector to prefix rather than to echo.

- [ ] **Step 2: run, fail. Step 3: validation against the live provider**

The handler validates by asking the provider for one symbol **using the candidate key**, not the app key.
`FinnhubQuoteProvider.SymbolExistsAsync` deliberately fails *open* — a provider outage must not block someone
recording a purchase — and that is exactly wrong here, where an unanswerable check must not be read as a
valid key. So this needs its own narrow method rather than reuse:

Add to `IQuoteProvider`:

```csharp
/// <summary>Answers whether the provider accepts this key. Unlike every other method here, it does not fail open.</summary>
Task<KeyVerdict> VerifyKeyAsync(string apiKey, CancellationToken ct);
```

with `public enum KeyVerdict { Accepted, Rejected, Unknown }` in `MarketData.Application`.
`FinnhubQuoteProvider` calls `/search?q=AAPL` with the candidate key in the `X-Finnhub-Token` header of the
request message: a 2xx is `Accepted`, a 401 or 403 is `Rejected`, anything else — timeout, 5xx, an open
circuit — is `Unknown`.

**`FakeQuoteProvider` needs a rule that can say no, and this is the thing most likely to be got wrong.**
`MarketDataModule.cs:41-63` registers `FinnhubQuoteProvider` **only when a key is configured**, so on the
clean-clone path — no `Finnhub__ApiKey`, which is the `docker compose up` default and what `ApiFixture`
pins (`ApiFixture.cs:312`) — the fake is the only provider there is. A fake that accepts every key makes the
rejected and unanswerable arms unreachable in every test and in every local demo.

So: the fake `Accepts` any key of at least sixteen characters, `Rejects` anything shorter, and `Rejects` the
literal `"unknown"` sentinel — no `Unknown` verdict, because a fake that pretends the provider is down is a
fake nobody can reason about. The `Unknown` arm is reached in tests through `ScriptedQuoteProvider`
(`tests/…/Infrastructure/ScriptedQuoteProvider.cs`), which must gain the new `VerifyKeyAsync` member and a
way to script each of the three verdicts.

State this rule in the fake's own doc comment. It is invented behaviour, and invented behaviour that nothing
explains gets "simplified" back to always-true.

The handler maps `Unknown` to a distinct failure record, not to success and not to `ProviderRejectedTheKey`:
telling someone their key is bad when the provider was merely down is the same class of mistake as the
`c: 0` trap.

Handler signature:

```csharp
Task<OneOf<SaveApiKeyResult, ProviderRejectedTheKey, ProviderCouldNotAnswer, ByokDisabled>>
```

mapped to 200 / 400 / 503 / 404 respectively. The 404 for a disabled feature is deliberate: a switched-off
feature should not advertise itself.

- [ ] **Step 4: the feature switch**

One boolean, `MarketData:Byok:Enabled`, defaulting to **true** in code and set explicitly in
`appsettings.json`. A `0`-or-blank placeholder is not acceptable here — `CLAUDE.md` records a placeholder in
`appsettings.json` silently overriding a code default and rejecting everything.

Thread it: `.env.example` (`BYOK_ENABLED=true`), `docker-compose.yml` (`MarketData__Byok__Enabled`),
`infra/main.bicep` (a plain `param byokEnabled bool = true`, not a secret), `containerapp-api.bicep`
(`baseEnv`, plain `value:`), and `deploy.yml`'s parameter JSON.

- [ ] **Step 5: repository, reader, endpoints**

`MarketDataEndpoints.cs` has **no route group at all** — it maps straight off `app` at lines 62, 73 and 89,
so there is no shared `RequireAuthorization`, no shared 401 and no shared 500. Create the group here, shaped
exactly like Identity's in Task 1 Step 10:

```csharp
var settings = app.MapGroup("/api/settings")
    .WithTags("Settings")
    .RequireAuthorization()
    .ProducesProblem(StatusCodes.Status401Unauthorized)
    .ProducesProblem(StatusCodes.Status500InternalServerError);
```

Three modules now map a group at `/api/settings`. Update Task 2 Step 8's note, which says two.

```csharp
settings.MapGet("/api-key", GetApiKeyStatusAsync)
    .WithName("GetApiKeyStatus")
    .Produces<GetApiKeyStatusResult>(StatusCodes.Status200OK);

settings.MapPost("/api-key", SaveApiKeyAsync)
    .AddEndpointFilter<ValidationFilter<SaveApiKeyRequest>>()
    .WithName("SaveApiKey")
    .Produces<GetApiKeyStatusResult>(StatusCodes.Status200OK)
    .ProducesValidationProblem()
    .ProducesProblem(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

settings.MapDelete("/api-key", RemoveApiKeyAsync)
    .WithName("RemoveApiKey")
    .Produces(StatusCodes.Status204NoContent)
    .ProducesProblem(StatusCodes.Status404NotFound);
```

`SaveApiKeyRequest(string ApiKey)`; the validator checks non-blank and a sane length ceiling and nothing
about the format — the provider is the authority on what a valid key looks like, and guessing a regex here
would reject keys that work.

- [ ] **Step 6: integration tests**

`ApiKeyTests.cs`, with a scripted provider swapped in the way `ScriptedQuoteProvider` already is:

- `Post_WithAKeyTheProviderAccepts_Returns200WithLastFourOnly`
- `Post_WithAKeyTheProviderRejects_Returns400AndStoresNothing`
- `Post_WhenTheProviderCannotAnswer_Returns503AndStoresNothing`
- `Get_AfterSaving_NeverReturnsTheKeyAnywhereInTheBody` — assert the raw response text does not contain the
  key. Assert on the **whole body string**, not on a deserialised field; a leak added later will be in a
  field this test does not know to look at.
- `Delete_ThenGet_ReportsNotConfigured`
- `Post_WhenByokIsDisabled_Returns404`

- [ ] **Step 7: `ExpectedRouteNames["MarketData"]`, build, test, commit**

`git commit -m "A user can bring their own provider key, and the server never gives it back"`

---

## Task 7 — Bring your own key: use it

**Files:**
- Modify: `…MarketData.Contracts/IQuoteReader.cs`, `…MarketData.Application/Abstractions/IQuoteProvider.cs`, `…MarketData.Application/Prices/QuoteReader.cs`, `…MarketData.Infrastructure/Quotes/FinnhubQuoteProvider.cs`, `…/Quotes/FakeQuoteProvider.cs`, `…MarketData.Infrastructure/Polling/QuotePoller.cs`
- Modify: `…Portfolio.Application/Dashboard/Queries/GetDashboard/GetDashboardQueryHandler.cs`
- Test: `tests/StockPortfolio.Modules.MarketData.UnitTests/QuoteReaderTests.cs` (extend), `…/FinnhubQuoteProviderTests.cs` (extend), `tests/StockPortfolio.Api.IntegrationTests/DashboardTests.cs` (extend)

**Interfaces produced:**

```csharp
// MarketData.Contracts — records of primitives only, so a raw Guid.
// The return type is a DICTIONARY keyed by ticker and must stay one.
Task<IReadOnlyDictionary<string, QuotedPrice>> GetCurrentPricesAsync(
    Guid userId, IReadOnlyCollection<string> tickers, CancellationToken ct);

// MarketData.Application.Abstractions
Task<IReadOnlyList<Quote>> GetQuotesAsync(
    IReadOnlySet<Ticker> tickers, string? apiKeyOverride, CancellationToken ct);
```

**Only the parameter list changes. Do not touch the return type.** `QuoteReader.cs:20` builds the dictionary
so the last-known-price fallback can take a set difference at `:52`, and `GetDashboardQueryHandler.cs:28`
passes `ReadOnlyDictionary<string, QuotedPrice>.Empty` into `DashboardCalculator`. Flattening it to a list
breaks the per-ticker degradation path — the one `CLAUDE.md` singles out as the mistake that looked identical
to the correct implementation in every test until `IsLastKnown` was asserted.

- [ ] **Step 1: the failing tests**

```csharp
[Fact]
public async Task GetCurrentPrices_WhenTheUserHasTheirOwnKey_PassesItToTheProvider()
{
    var provider = new RecordingQuoteProvider();
    var reader = AReader(provider, keyFor: user => "user-key-a1b2");

    await reader.GetCurrentPricesAsync(AUser, ["AAPL"], TestContext.Current.CancellationToken);

    provider.LastApiKeyOverride.ShouldBe("user-key-a1b2");
}

[Fact]
public async Task GetCurrentPrices_WhenTheUserHasNoKey_LeavesTheProviderOnTheApplicationKey()
{
    var provider = new RecordingQuoteProvider();
    var reader = AReader(provider, keyFor: _ => null);

    await reader.GetCurrentPricesAsync(AUser, ["AAPL"], TestContext.Current.CancellationToken);

    provider.LastApiKeyOverride.ShouldBeNull();
}

[Fact]
public async Task Poll_Always_LeavesTheProviderOnTheApplicationKey()
{
    // The shared window is shared. A user's quota must not be spent filling it.
}
```

- [ ] **Step 2: run, fail. Step 3: thread it**

`GetDashboardQueryHandler` already holds `query.UserId` and drops it. Pass it:

```csharp
var prices = await quotes.GetCurrentPricesAsync(query.UserId, tickers, ct);
```

`QuoteReader` gains `IUserProviderKeyReader` and resolves the key once per call, before the fan-out — not per
ticker. It is one database read and one unprotect for a whole dashboard load.

`ISymbolValidator` and `ICompanyNameReader` are **not** changed. Adding a holding and looking up a company
name stay on the application's key, exactly as the phase plan says: the user's key is for their own dashboard
fetches.

- [ ] **Step 4: the provider**

`FinnhubQuoteProvider` currently relies on `client.DefaultRequestHeaders`. Switch the per-ticker call to an
explicit `HttpRequestMessage` and set `X-Finnhub-Token` on it when an override is present — a header on the
request wins over the client's default, which is what makes this work without a second `HttpClient`.

Then decision D3, which has **two halves**. The first is the token bucket:

```csharp
// The bucket is sized for the application's key. A user who brought their own brought their own quota
// with it, and must not be throttled against a budget that is not theirs.
RateLimitLease? lease = apiKeyOverride is null ? await budget.AcquireAsync(1, ct) : null;
```

Guard the `lease.IsAcquired` check and the disposal on `lease is not null`.

The second half is the one the first draft of this plan missed, and it is the more damaging of the two.
`MarketDataModule.cs:50` attaches `AddStandardResilienceHandler` to the one named `HttpClient`, with
`MinimumThroughput = 10` over a 30-second window (`:105-118`). Every bring-your-own-key request would
otherwise share that breaker — so a single user whose key is revoked produces ten 401s in thirty seconds,
opens the circuit, and `FinnhubQuoteProvider.cs:57` then catches `BrokenCircuitException` per ticker and
returns nothing **for everybody**, including the poller. One user's bad key would blank every dashboard.

So register a **second named client** in `AddMarketDataModule`, with the same `ConfigureResilience` and no
default token header:

```csharp
// A separate client, so a user's revoked key trips a breaker that is theirs alone.
services.AddHttpClient(ByokClientName, client => client.BaseAddress = new Uri(options.BaseUrl))
    .AddStandardResilienceHandler(ConfigureResilience);
```

`FinnhubQuoteProvider` takes `IHttpClientFactory` alongside its typed client and picks: the typed client when
`apiKeyOverride is null`, `factory.CreateClient(ByokClientName)` otherwise. Named clients each get their own
handler chain, so each gets its own breaker — that is the whole point, and it is why a per-request header on
the shared client is not sufficient on its own.

`FakeQuoteProvider` takes the parameter and ignores it, with a one-line comment saying so — an ignored
parameter with no comment reads as an oversight.

`QuotePoller` passes `null`. That is now structural rather than a convention: the poller has no user, so it
has no key to pass.

- [ ] **Step 4b: a revoked key must say so**

When a bring-your-own-key fetch comes back 401 or 403, `QuoteReader` calls
`IUserProviderKeyRepository.MarkRejectedAsync(userId, ct)`, which sets `LastRejectedAt`. The status endpoint
from Task 6 then reports `Rejected: true` and the settings screen tells the user to re-enter it.

Without this, revocation is invisible: the provider returns null, the dashboard falls back to last-known
prices flagged `IsLastKnown`, and the user sees stale numbers with no cause. Test it —
`GetCurrentPrices_WhenTheProviderRefusesTheUsersKey_MarksItRejected` — because nothing about the happy path
would ever reveal the omission.

- [ ] **Step 5: run the tests. Step 6: an integration test**

Extend `DashboardTests.cs` with `Dashboard_ForAUserWithTheirOwnKey_UsesThatKey`, asserting through the
scripted provider which key it saw. Also assert the existing degradation test still passes — a per-user key
must not change what happens when three of twenty tickers fail, and `Dashboard_ProviderReturns429_Returns200NotError`
already asserts `IsLastKnown == false` on the served symbols for exactly that reason.

- [ ] **Step 7: build, test, commit**

`git commit -m "A user's own key prices their own dashboard, and nothing else"`

---

## Task 8 — The theme, applied before the first paint

**Files:**
- Modify: `src/Web/index.html`, `src/Web/src/index.css`
- Create: `src/Web/src/lib/theme.ts`
- Test: `src/Web/tests/theme.test.tsx`

- [ ] **Step 1: change the variant — and the palette block, which is a second place**

`src/index.css:14` today is:

```css
@custom-variant dark (&:where(.dark, .dark *));
```

Change it to key off an attribute, which is what the inline script can set before React exists:

```css
@custom-variant dark (&:where([data-theme="dark"], [data-theme="dark"] *));
```

**`src/index.css:38` is `.dark {` and must change too**, to `[data-theme="dark"] {`. That block holds the
actual palette — `--bg`, `--panel`, `--panel-2`, `--bd`, `--tx`, `--mu`, `--up`, `--dn`. Change line 14 alone
and the `dark:` utilities follow the attribute while every colour variable still waits for a `.dark` class
that nothing sets any more: the app renders the light palette in **both** modes, with no error and no warning.

The comment at `index.css:11` says "this one line is what changes". It is wrong — there are two — so fix the
comment as well as the code, and make it describe what the file now does rather than what it will do.

- [ ] **Step 2: the inline script**

In `index.html`, replace `<html lang="en" class="dark">` with `<html lang="en">` and put this in `<head>`,
**before anything that loads CSS**:

```html
<script>
  // Runs before the first paint. Without it every load flashes light before React mounts.
  // The server is the source of truth; this cache exists only so there is something to read
  // synchronously, because a fetch cannot happen before paint.
  (function () {
    try {
      var stored = localStorage.getItem('stockportfolio.theme') || 'system';
      var dark = stored === 'dark' ||
        (stored === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches);
      document.documentElement.setAttribute('data-theme', dark ? 'dark' : 'light');
    } catch (e) {
      document.documentElement.setAttribute('data-theme', 'dark');
    }
  })();
</script>
```

The `catch` matters: `localStorage` throws in a browser with site data blocked, and an exception here leaves
the page unstyled rather than merely wrongly themed.

Same file, line 6: `<meta name="color-scheme" content="dark light" />` puts **dark first**, so native
`<select>`, `<input>` and scrollbars keep rendering dark even in light mode. The brief bans UI kits, so those
native controls *are* this app's form controls. Either flip it to `light dark` or have the inline script set
`document.documentElement.style.colorScheme` alongside the attribute. The second is better — it stays correct
in all three modes rather than being right in one and tolerable in the other.

- [ ] **Step 3: `src/lib/theme.ts`**

Exports `type ThemeChoice = 'light' | 'dark' | 'system'`, `readCachedTheme()`, `cacheTheme(choice)`,
`applyTheme(choice)` and `watchSystemTheme(onChange)`. `watchSystemTheme` adds a `change` listener to the
`prefers-color-scheme` media query and returns its own teardown — React 19 StrictMode runs effects twice, so
a hook that does not tear down leaves two listeners.

- [ ] **Step 4: the tests**

`tests/theme.test.tsx`:

- `applyTheme_WithDark_SetsTheDocumentAttribute`
- `applyTheme_WithSystem_FollowsTheMediaQuery`
- `watchSystemTheme_WhenTheOsThemeChanges_CallsBack` — drive it by dispatching a `change` on a stubbed
  `matchMedia`
- `watchSystemTheme_AfterTeardown_DoesNotCallBack` — this is the StrictMode double-mount case, and the only
  one that catches a missing cleanup

- [ ] **Step 5: run, commit**

```bash
npm --prefix src/Web test
```

`git commit -m "The theme is an attribute, applied before the page paints"`

---

## Task 9 — English and Ukrainian

The Phase 5 plan says validation messages translate with no changes to the forms. **That is true of three
forms of five.** `portfolio.tsx`, `EditHoldingForm.tsx` and `AlertSettingsForm.tsx` already use message keys;
`login.tsx` and `register.tsx` use English sentences. And every static string in every component — nav
labels, table headers, button text — is a hardcoded English literal. Budget for a sweep, not a wiring job.

**Files:**
- Modify: `src/Web/package.json`
- Create: `src/Web/src/lib/i18n.ts`, `src/Web/src/locales/{en,uk}/{common,auth,portfolio,dashboard,alerts,settings,errors}.json`
- Create: `src/Web/scripts/check-locale-parity.mjs`
- Modify: `src/Web/src/main.tsx`, `src/Web/src/routes/login.tsx`, `src/Web/src/routes/register.tsx`, `src/Web/src/lib/format.ts`, every component holding a literal
- Modify: `.github/workflows/ci.yml`
- Test: `src/Web/tests/i18n.test.tsx`

- [ ] **Step 1: install**

```bash
npm --prefix src/Web install i18next react-i18next
```

Then **read the installed versions out of `package.json`** and confirm `i18next` is 26.2 or later. react-i18next
17 against an older i18next fails at runtime, not at install, so a green `npm ci` proves nothing. Pin both to
exact versions the way every other dependency here is pinned.

- [ ] **Step 2: the parity check, written before the translations**

`scripts/check-locale-parity.mjs` reads every file under `src/locales/en` and `src/locales/uk`, flattens each
to a set of dotted key paths, and exits non-zero listing any key present in one and missing from the other.

There is **no fallback to English.** A missing Ukrainian key must render as a raw key path, which is ugly and
visible, rather than as English, which hides the bug from whoever added it and shows it to everyone else.

```bash
node src/Web/scripts/check-locale-parity.mjs
```

Expected at this point: passes trivially over two empty trees. Now delete a key from one side and watch it
fail, naming the key. A check that has never been seen red is not a check.

- [ ] **Step 3: add it to CI**

In `ci.yml`'s `web` job, after `npm ci` and before `npm test`, guarded by the same
`steps.spa.outputs.present == 'true'` condition every other step in that job uses.

- [ ] **Step 4: i18next setup**

`src/lib/i18n.ts`: resources imported statically (seven namespaces × two languages is small enough that lazy
loading buys nothing and costs a loading state), `fallbackLng: false`, `interpolation: { escapeValue: false }`.

**The detector caches to browser storage and will override the server on the next load.** Configure it as a
pre-sign-in bootstrap only: read the cache to pick a language before the session exists, and call
`i18n.changeLanguage(...)` with the server's value as soon as the appearance query resolves. Write the test
for that before the code — it is the failure that only shows up on the second page load.

- [ ] **Step 5: rewrite the two form schemas**

`login.tsx` and `register.tsx` currently read like `z.email('Enter a valid email address.')`. Change every
message to a key — `z.email('errors.email.format')`, `z.string().min(8, 'errors.password.tooShort')` — matching
the convention the other three forms already follow, and add those keys to `errors.json` in both languages.
The comment in `portfolio.tsx:73-79` explains the convention; do not invent a second one.

- [ ] **Step 6: the sweep**

Replace every user-visible literal with `t('namespace:key')`. Work file by file, committing per feature area,
so a reviewer can follow it. `AppShell.tsx`'s `NAV` array, `Table.tsx` column headers wherever they are
passed in, `Button`/`Alert`/`Card` copy, and every heading.

**This sweep runs before Task 10 exists, so it cannot cover it.** The settings screen introduces a good deal
of new copy — three distinct key-failure messages, the refresh-interval honesty paragraph, "showing 6 of 8",
"configured, ends a1b2" — and the parity check passes happily over English-only strings that were never added
to either locale file. Task 10 Step 7 therefore carries its own sweep, and the parity check is not what
catches a missed one. Write settings copy as keys from the first line rather than as literals to be swept
later.

- [ ] **Step 7: number and date formatting**

`src/lib/format.ts` calls `new Intl.NumberFormat(undefined, …)`, which means "browser default". Pass the
chosen language instead. **The currency stays US dollars** — only the presentation localises, and
`formatMoney` still formats the string that came off the wire without ever parsing it into arithmetic.

- [ ] **Step 8: the test**

`tests/i18n.test.tsx`:

- `switchingLanguage_ToUkrainian_TranslatesNavigationAndTableHeaders`
- `switchingLanguage_ToUkrainian_TranslatesAValidationMessage` — submit an invalid form and assert the
  Ukrainian text, which is what proves the key convention actually reaches the user
- `reload_AfterChoosingUkrainian_StaysUkrainian`
- `serverLanguage_DisagreeingWithTheCache_Wins`

- [ ] **Step 9: run everything, commit**

```bash
npm --prefix src/Web test && node src/Web/scripts/check-locale-parity.mjs
```

---

## Task 10 — The settings screen

**Files:**
- Create: `src/Web/src/settings/settingsApi.ts`, `AppearanceSection.tsx`, `LanguageSection.tsx`, `QuotesSection.tsx`, `ApiKeySection.tsx`, `VisibilitySection.tsx`
- Create: `src/Web/src/routes/_authenticated/settings.tsx`
- Modify: `src/Web/src/components/AppShell.tsx`
- Create: `src/Web/tests/settings.test.tsx`, `src/Web/tests/msw/settings.ts`

**Interfaces consumed:** the four `GET`s from Tasks 1, 2, 6 and the existing `GET /api/alerts/settings`.

- [ ] **Step 1: `settingsApi.ts`**

Types, a `settingsKeys` factory and one fetcher per route, following `alertsApi.ts` exactly — that file is
the closest existing model, since it already has both queries and mutations against one feature.

- [ ] **Step 2: the route, with five sections in the plan's order**

Appearance, language, quotes (refresh interval **and** the alert threshold), your own key, then the
visibility list.

**Each section saves on its own**, with its own inline saved-or-failed state. One form with one Save button
would let a rejected API key throw away a perfectly good theme change — which is the reason the API has
targeted writes rather than one big `PUT`.

- [ ] **Step 3: the honesty line on the refresh interval**

The quotes section must say plainly that a shorter interval costs more provider calls against a shared quota.
Every refresh is a real fan-out — one call per visible position. Twenty positions at sixty seconds is twenty
calls a minute out of roughly sixty, for one viewer.

Do not lower the 60-second default to make that arithmetic look better. The 10-to-300 range is not the
problem; what the screen claims is.

- [ ] **Step 4: the key field**

A `type="password"` input. Once set it reads "configured, ends a1b2" with a Remove button. Saving shows a
spinner while the server checks the key, then either succeeds or gives the specific reason — and "the
provider could not answer" is a different message from "the provider rejected your key".

- [ ] **Step 5: the visibility list**

A checkbox per position with a "showing 6 of 8" counter and a Show all link. Each toggle is its own
`PATCH`, optimistic, with the snapshot-and-rollback pattern `useHoldingMutations.ts` already uses. Note that
TanStack Query 5.89 renamed the `TContext` generic and added a trailing `context` argument, but **argument
positions did not move** — the `onMutate` snapshot is still argument 3 in `onError`.

- [ ] **Step 6: nav and mobile**

Add a Settings entry to `AppShell.tsx`'s `NAV`. Check the screen at 375px wide; it is in the "Done when" list.

- [ ] **Step 7: translate this screen's own copy, then test**

Every string added in this task goes into `settings.json` and `errors.json` in **both** languages as it is
written, not afterwards. Run `node src/Web/scripts/check-locale-parity.mjs` — it proves the two files agree
with each other, and proves nothing at all about a string that was never added to either. The only check on
that is reading the screen with Ukrainian selected, which is Step 8.

`tests/msw/settings.ts` with one handler per route, then `tests/settings.test.tsx`:

- `savingTheTheme_WhenTheApiKeySectionIsFailing_StillSaves` — the reason sections save separately
- `hidingAPosition_UpdatesTheCounterAndTheDashboard`
- `theApiKey_IsNeverPresentInAnyResponseTheScreenReceives`
- `changingTheRefreshInterval_ChangesHowOftenTheDashboardRefetches` — drive it with fake timers

- [ ] **Step 8: run, read the screen in Ukrainian, commit**

---

## Task 11 — The dashboard obeys the saved interval

Today the interval is `useState(DEFAULT_INTERVAL_MS)` local to `dashboard.tsx`, with choices of 15s / 30s /
60s / 5m and no persistence.

**Files:** modify `src/Web/src/routes/_authenticated/dashboard.tsx`; test `src/Web/tests/dashboard.test.tsx`

- [ ] **Step 1:** the failing test — `dashboard_WithASavedIntervalOfFifteenSeconds_RefetchesEveryFifteenSeconds`,
  using vitest fake timers and counting MSW hits.
- [ ] **Step 2:** run, fail.
- [ ] **Step 3:** source `refetchInterval` from the dashboard-settings query instead of local state. Keep the
  in-page `<select>` as a shortcut, but make it write through the same mutation the settings screen uses, so
  the two cannot disagree. Widen the choices to cover the 10–300 range the server accepts.
- [ ] **Step 4:** run, pass, commit.

---

## Task 12 — Documents, deploy, and verify in a browser

A phase is done when it runs in a browser **and is deployed**, not when its tests pass.

- [ ] **Step 1: fix what is already stale**

`docs/plan/00-overview.md:34` still says Phase 4 is "Built and verified locally; **not deployed, so not
done**". It was deployed on 2026-08-06. Also update the "Known gaps" bullet saying Phase 4 has not been
deployed.

- [ ] **Step 2: update the reference documents**

- `docs/reference/er-diagram.md` — three new tables and MarketData's first schema with real contents.
- `docs/reference/module-boundaries.md` — who owns which setting; MarketData no longer the module that
  persists nothing.
- `docs/reference/service-interactions.md` — the per-user key on the dashboard read path.

- [ ] **Step 3: the README**

Four things the "Done when" list names explicitly: who owns which setting; what a shorter refresh interval
actually costs; how a user's own key is used and where the application's key is still used; and what hiding
a position does and does not affect — in particular that alerts still fire on a hidden position, which is
the first thing a reviewer asks.

- [ ] **Step 4: `CLAUDE.md`**

The counts change: assemblies stay at 22, but the migrated contexts go from three to **four**, and the
connection-pool arithmetic goes from 3 × 2 × 2 = 12 to **4 × 2 × 2 = 16** of the tier's 35. That figure has
been published wrong before — count `AddDbContext` calls, do not restate it from memory. Update the test
counts from a real run, and record the new traps this phase found.

- [ ] **Step 5: `docs/deferred-work.md`**

Add anything cut here with a trigger a later reader can check. At minimum the two options rejected in D1 and
D5.

- [ ] **Step 6: the whole stack, from clean**

```bash
docker compose down -v && docker compose up
```

Then walk the "Done when" list in a browser: dark applies instantly, reload shows **no flash**, follow-the-
system reacts to an OS theme change without a reload, Ukrainian survives a reload, 15 seconds visibly
refetches faster, a 2% threshold fires, hiding a position removes the row and updates the counter, and a
hidden position still alerts.

**Two items on that list cannot be checked here, and this is not a shortcoming of the environment.** With no
`Finnhub__ApiKey`, `MarketDataModule.cs:41-63` registers `FakeQuoteProvider`, so "a bad key is rejected with
a specific message" and "a good key is stored and used" are being answered by a fake with an invented rule,
not by the provider. The compose walk proves the *plumbing* — a short key is refused, a long one is stored,
the response body never contains it. Whether a genuine Finnhub key is accepted and a genuine bad one refused
is provable only in Step 8, against the deployed API, which has a real key. Say which is which in the
verification notes rather than ticking both here.

The same split applies to the encryption: locally, restart the API container and confirm a stored key still
decrypts. That exercises the key ring surviving a process restart, which is what the Postgres requirement is
for, and it is the one part of bring-your-own-key that compose *can* prove properly.

- [ ] **Step 7: deploy**

Push the branch, open the pull request, merge to `main`. **Deploying means pushing to `main` and nothing
else** — never run `az deployment group create` by hand. Read `docs/DEPLOYING.md` first.

- [ ] **Step 8: verify on the public URL**, then repeat the browser walk there — including the two items
  Step 6 could not answer: paste a genuinely invalid provider key and confirm the specific rejection, then
  paste a valid one and confirm the dashboard prices through it and the key never appears in any network
  response.

Also fix the stale comment at `src/Api/Program.cs:93-95`, which claims MarketData "registers no
`ICommandHandler` or `IQueryHandler` at all". That was already false — `MarketDataModule.cs:76-78` registers
`SearchTickersQueryHandler` — and Task 6 adds three more.

- [ ] **Step 9: delete this file.** An implementation plan is written when a phase starts and deleted when it
  ships. `docs/plan/` goes back to seven files.

---

## Open questions this plan does not answer

- **`TokenPolicy` values remain provisional and unsigned-off.** Unchanged by this phase, still open.
- **The readiness probe checks one database login of four now, not three** (`deferred-work.md` C7). Adding
  MarketData's context widens that gap by one. It is not this phase's job to close it, but the entry needs
  its status re-read when this phase closes.
- **`ISymbolValidator` and `ICompanyNameReader` stay on the application's key.** If a user's quota should
  cover ticker search too, that is a second decision and a second change; it is deliberately out of scope
  here because the phase plan scopes the key to "that user's own dashboard fetches".
