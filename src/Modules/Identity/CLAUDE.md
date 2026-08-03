# Identity — the reference implementation

The rules are in [../CLAUDE.md](../CLAUDE.md). This file is the index: which file to open when you want to
see a rule actually working, and why that file rather than a sibling.

**Read `../CLAUDE.md` §3 before copying anything from here.** Five of Identity's answers are wrong for a
module that has domain events, a background service, or an outbound HTTP dependency.

---

## Open this file to see that concept

| Concept | File | Why this one |
|---|---|---|
| Entity shape | `Domain/User.cs` | The private all-args constructor, the guard-free rule, and a factory returning `OneOf<User, InvalidInput>` — all in 90 lines |
| Two endings for one field | `Domain/RefreshToken.cs` | `Supersede` and `Revoke` both stamp `SupersededAt`; only one sets `SupersededBy`. Conflating them made logout do nothing for 30 seconds |
| Strongly-typed id | `Domain/UserId.cs` | 6 lines. `Guid.CreateVersion7()` in the domain, because Npgsql's sequential generator selects on `ClrType` and would never fire for `UserId` |
| Use-case folder | `Application/Authentication/Commands/RegisterUser/` | Command, handler, and the one failure record that belongs to it — nothing pooled |
| Handler returning a union | `Application/.../RegisterUser/RegisterUserCommandHandler.cs` | `OneOf<TokenPair, EmailAlreadyUsed, InvalidInput>` in the signature; the context question asked *before* the expensive hash |
| A collaborator shared by handlers | `Application/Authentication/SessionOpener.cs` | Two public names (`OpenAsync`, `RotateAsync`) over one private core — not one method with a nullable flag. Holds four invariants that fail silently if copied |
| Layering seam | `Application/Abstractions/IUserRepository.cs` | Names the commit point in the doc comment, which is the thing Portfolio must change |
| Module DI seam | `Infrastructure/IdentityModule.cs` | The only public type in `.Infrastructure`, and the loud failure when a connection string is missing |
| Handler registration | `Infrastructure/DependencyInjection.cs` | Closed generics spelled out — the cost of returning `OneOf<…>` directly, and worth it |
| EF configuration | `Infrastructure/Persistence/Configurations/UserConfiguration.cs` | Explicit column names, and the unique index that backstops the handler's check |
| Value converter | `Infrastructure/Persistence/Converters/UserIdConverter.cs` | Lives in `.Infrastructure`, not beside the id — this is what keeps EF out of `.Domain` |
| Request + validator | `Api/Requests/RegisterUserRequest.cs` + `Api/Validators/RegisterUserRequestValidator.cs` | Cross-field rule (password ≠ email) that DataAnnotations cannot express |
| Endpoint surface | `Api/IdentityEndpoints.cs` | Five routes, every status declared, `.Match<IResult>` with named parameters, group-level 500 |

## And in tests

| Concept | File |
|---|---|
| A rule that would otherwise pass by finding nothing | `Architecture.Tests/ModuleBoundaryTests.cs` → `EmptyShells_AreExactlyThePhasesNotYetBuilt` |
| `.Produces` tied to observed behaviour | `Api.IntegrationTests/EndpointMetadataTests.cs` |
| Parameterisation proved, not asserted | `Api.IntegrationTests/ParameterisationTests.cs` — the best-constructed test in the suite |
| Time-dependent behaviour | `Api.IntegrationTests/RefreshRotationTests.cs` — `FakeTimeProvider`, never `Thread.Sleep` |
| EF constructor binding | `Modules.Identity.UnitTests/EfConstructorBindingTests.cs` |

---

## What Identity does not have

It is a misleading teacher precisely because it is clean. It has **no** domain events, **no** background
service, **no** outbound HTTP, **no** Redis, **no** SSE, **no** cross-module dependency, and an **empty**
`.Contracts` — deliberately, because nothing calls Identity at runtime. The JWT carries the user id, which
is the argument for extracting Identity first.

So Identity cannot teach you: transaction scope under an event dispatcher, degradation as a success field,
authentication without a header, or an adapter over another module's contracts. Those rules are in
`../CLAUDE.md` anyway, sourced from the phase plans rather than from this code.

## Known provisional

`Application/TokenPolicy.cs` — the four values (15 min / 14 days / rotate on / 30 s grace) are the repo
owner's call and still marked provisional. `RotateOnUse` was deleted as a dead branch; if a second value is
ever genuinely needed, reintroduce it with both paths tested.
