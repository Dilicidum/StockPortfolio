# Phase 1 — Sign in

## Goal

Register, sign in, and stay signed in across a hard refresh — locally with one `docker compose up`, and on a public URL. Signing out makes a protected page bounce to the login page.

This phase carries **all** the plumbing: solution layout, container build, database bootstrap, test suites, cloud deployment, pipelines. Every later phase adds a delta. Infrastructure discovered on the last day is infrastructure that sinks the project.

It covers the acceptance items for authentication and session persistence, the routing half of the frontend requirement, parameterised database access, and the one-command local stack.

---

## How the code is arranged

A module is five projects: contracts, domain, application, infrastructure, HTTP. Four modules are designed; this phase builds one for real and leaves the others as empty shells, so the boundary rules have more than one module to check and later phases add files rather than plumbing.

**References point inward, and two of the edges are the point of the split.** Infrastructure never references the web framework. The HTTP layer never references the database or its own infrastructure. They meet only through interfaces declared in the application layer. Both rules are enforced by project references and asserted by reflection tests, so a route physically cannot reach a database context without passing through a handler.

**Inbound HTTP is presentation, not infrastructure.** An earlier shape put routes beside the database code to save a project, which forced one project to carry both the web framework and the ORM — the exact mixing the layering exists to prevent. Moving routes up into the host would also have fixed the layering, but then the host becomes the file every future feature edits and a module stops being something you could lift out whole. So each module owns its HTTP project: four extra project files, in exchange for two rules the compiler checks for free.

**Accessibility follows the onion; it is not a blanket `internal`.** The original rule — everything internal outside contracts — cannot compile, because `internal` is per-assembly and a module is five assemblies. Contracts, domain, application and HTTP are public; infrastructure is internal apart from the one registration entry point the host calls. Infrastructure is where leaks actually happen: nothing outside a module should name its database context, repositories or password hasher. The HTTP layer is public because it is a leaf only the host references, and because serialisation and the API-document generator behave better with public types. The cost is real: the compiler no longer stops one module reaching into another's internals, so the architecture tests are the only enforcement left.

The shared kernel holds money, the one shared validation-failure record, and the two handler interfaces. It stays framework-free, so anything naming a web-framework type lives in a separate shared HTTP project. An interface for "a module that registers endpoints" was written and deleted: implemented once per module and called once per module by the host, it is the same list written twice.

There is no aggregate base class and no domain-event machinery. Both were written and removed here, because nothing raised an event — the event list was always empty and the type parameter existed only to satisfy the base class.

---

## How a use case is written

CQRS with no dispatcher. Handlers are injected straight into endpoints. There is one caller per handler, so a mediator would have nothing to decouple. Cross-cutting behaviour is a dependency-injection decorator instead; logging is the only one needed here.

A handler returns a union of its outcomes directly in its signature. An earlier shape wrapped each union in a named result class; the wrapper hid the thing a reader most wants to see. Matching over a union takes one delegate per case, so adding an outcome breaks every call site — exhaustiveness comes from that, not from a wrapper or an analyser.

**Validation happens in three places, and only one uses result types.**

| Kind | Where | What happens |
|---|---|---|
| Shape — is this even an email? | the module's HTTP layer | 400 with field-level problem details |
| Context — does this account exist, is this allowed? | the handler | a failure case in the returned union |
| Invariant — a user can never have a blank email | the entity | throws |

Shape validation is an endpoint filter, not a dependency-injection decorator. A decorator would have to manufacture a failure value of an unconstrained result type — impossible in general, and impossible in particular for a login whose outcome union has no shape-failure case at all. A filter sits in the HTTP pipeline and can simply return a 400.

It validates the **request** bound off the wire, not the command; the application layer never binds off the wire. The two records look nearly identical today, and that is fine — the wire contract and the use-case input are free to move apart, and only the request appears in the published API document. The consequence: a non-HTTP caller of a handler would skip the shape rules. There is one caller per handler today, so it costs nothing; a background job calling a handler directly would need its own guard.

The framework's built-in attribute-driven validation was evaluated and rejected: fine for "required" and "looks like an email", awkward the moment a rule is conditional, spans two fields, or needs a lookup. Validators are injected one at a time, never as a collection — the collection form silently validates nothing when a validator is missing.

---

## Entities

An entity has exactly one constructor: private, taking every mapped value, assigning and doing nothing else. No parameterless constructor, no object initialiser, no settable property, so a half-built entity cannot be represented and a static factory returning a union is the only way in.

An earlier version of this plan said the opposite — never write a constructor whose parameter names match mapped properties, because the ORM's binder will select it for loading rows. The hazard is real; the conclusion was wrong. The binder does pick that constructor, by parameter name, regardless of accessibility, and that is fine because it only assigns. What makes it a trap is putting a **guard** inside it: the guard then runs on every row of every read. Keep the constructor guard-free and validate in the factory, which the ORM never calls.

The sharp edge: binding is by name, so renaming a constructor parameter without renaming its property leaves nothing bindable, and with no parameterless fallback the whole model fails to build at startup rather than on first query. Validation in a setter would be dead code that looks alive, because the ORM writes the backing field and never calls the setter — which is why there is no settable surface at all.

Time comes from an injectable clock, never a static "now". Identifiers are UUIDv7, generated in the domain for index locality. Money is a decimal on the server, serialised as a string and never computed in the browser; its currency is normalised as the value is constructed, so the same currency in two casings compares and adds correctly.

---

## Registering and signing in

**"Is this address already taken?" is a context question, so the handler asks it.** It looks the address up and returns a conflict; it does not insert and read a unique-violation back out of an exception. The exception route was genuinely race-free and was rejected anyway, because it put the rule "an address may be used once" in the persistence layer as an error-code filter, while the file you read to learn what registration does never mentioned it.

The accepted cost, stated rather than hidden: two simultaneous registrations of one address can both pass the check, and the loser hits the unique index and surfaces as a 500 instead of a 409. The index stays — it is what keeps the data correct — and the window is a millisecond wide. The exception catch comes back only if that 500 is ever actually observed.

The lookup normalises through the same single definition of the canonical stored form used when the account was created; normalise differently and the lookup simply misses, which here also means a 500. The check runs before hashing, because hashing is deliberately slow and a taken address is a conflict whatever the password was.

**Login reports one undifferentiated failure**, not "no such user" plus "wrong password", because two cases leak whether an account exists. It also verifies against a fixed dummy hash when the account does not exist, so the timing does not leak what the body refuses to.

Passwords use Argon2id at the OWASP parameters with a random salt, encoded in the standard PHC string so the parameters travel with the hash and can be upgraded later. There is no Argon2 in the framework and none planned. Refresh tokens are opaque random values stored as a plain SHA-256 hash — no work factor, correct precisely because the token is already high-entropy: a slow hash over a random 256-bit value buys nothing and costs memory on every refresh.

The public surface is five routes: `POST /api/auth/register`, `/login`, `/refresh`, `/logout`, and `GET /api/auth/me`. The three that take a body carry the validation filter; the two that carry only a bearer token do not get an empty validator for symmetry.

---

## Sessions

**Rotation is unconditional.** Every use of a refresh token issues a new one and retires the old. A flag to turn that off was planned and never written — a switch whose only legal value is on is not a decision.

**A just-rotated token keeps working for a thirty-second grace period**, or two browser tabs refreshing at the same moment log each other out.

**Revoking a session and rotating it are genuinely different ends.** Both mark the old session superseded; only rotation records which session replaced it. A grace check written against "was it superseded?" alone keeps accepting the exact token the user just signed out with, for the whole window — sign-out silently does nothing for thirty seconds while every test stays green. The check asks whether a replacement was named, and only then allows the grace window.

Lifetimes: fifteen minutes for the access token, fourteen days for the refresh token. A short access token makes a leaked one a small window, and the extra round trips are cheap because concurrent refreshes collapse into one.

### In the browser

**The access token lives in a module-scoped variable and nowhere else.** A bearer sitting in web storage is readable by any injected script that runs once; in a closure, an attacker has to be live and resident.

**The refresh token lives in session storage, and there is no cookie in any deployment.** That is the honest consequence of where the halves are hosted: the site is static on one origin and the API is on another, so a cookie would be third-party and Safari blocks those outright. An earlier design claimed a dual mode — a same-origin cookie behind the local proxy, storage on the public site — and the cookie half was never built on either side. Adding a cookie later changes this decision, not just an implementation detail.

Session storage rather than local storage: it dies with the tab, so a shared machine does not hand a live fourteen-day session to the next person. Being tab-scoped means a second tab starts signed out, which is fixed by handing the session between tabs over a broadcast channel — not by moving the token somewhere shared.

**Concurrent refreshes collapse into one in-flight request**, or ten simultaneous 401s fire ten refreshes and nine race a rotated token into failure. The in-flight promise must also be cleared when it settles, or one failed refresh wedges the session forever.

**The session must be restored before the router mounts.** The route guard is synchronous and a React effect runs after the first render, so a session restored in an effect arrives too late and a hard refresh of a protected page always bounces to login. This is the most likely way the phase ships "done" and demos broken, because in-app navigation hides it completely. The app refreshes imperatively at startup, shows a splash while it settles, and mounts the router only once the answer is known.

The route base path derives from the build environment in one chain, so the router and the asset URLs cannot disagree: the local stack serves the app at the root, the public site under a repository path.

---

## The database

**One schema per module, and each module connects as its own database user.** A migrating role owns the schemas and is the only one that can create; each service role gets read-write on its own schema and nothing on anyone else's. Cross-schema access is revoked explicitly rather than merely not granted, so the boundary claim exists as executable SQL that survives someone adding a broad grant later. Default privileges are granted for future tables too — grant only on what exists today and the next migration produces tables the service role cannot read, which surfaces a phase later and looks like an ORM bug.

An integration test connects as one module's role, tries to read another module's tables, and asserts the specific permission error code rather than the message. That turns a design claim into a fact the pipeline re-checks. All four schemas and all five roles are created here, though only one module has tables, because that test needs a second role to exist.

**Every module's migration history table lives in that module's own schema.** Setting a default schema does not move it; without an explicit setting they share one table, each sees the others' migration identifiers, and the tooling reports migrations as applied-but-missing. It looks like corruption.

**Migrations are applied by a separate console program, never by the app at startup** — two replicas racing one migration corrupts the history table. The same program and image serve the local stack and the cloud job, so there is one code path.

**The database is reached only through the ORM's query API, never raw SQL.** The brief asks for parameterisation; going through the query API makes it structural. A command interceptor in the test fixture asserts no user-supplied value ever appears in the text of a command — the only way that requirement is visible to a reader. Connection pools are capped small: a burstable database allows few connections, and each distinct database user is a separate pool.

---

## The host

- Unhandled errors and bare status codes both become problem details, so a declared response body is always actually there.
- **Two health endpoints.** Liveness checks nothing; readiness checks the database and the cache. The platform restarts a container failing liveness, so a liveness probe touching the database turns a blip into a restart loop. The probes must also be declared explicitly in the infrastructure template, or the platform injects TCP probes, never calls either path, and the split is decorative.
- **Never enable response compression** — it buffers streaming responses, and a live feed dies silently.
- JSON numbers are handled strictly and money gets its own converter, rather than loosening a global setting. Inbound claim mapping is switched off explicitly, or the subject claim is renamed and reads back as null.
- **Exactly one CORS layer is active, and it is the application one.** Two layers on one response risk a duplicate allow-origin header, which browsers reject outright.

Configuration keys cross assembly boundaries freely; types do not. Anything the host must *name* has to be public, so it belongs in the module's one registration entry point; anything it only needs to *configure* travels as a configuration key.

---

## Infrastructure

Three targets, all established here: the local stack, the static site, and the API in a container app with a managed database and cache. Deployment happens by pushing to the main branch; the procedure, cost ceiling and failure history live in the deployment runbook and design record.

The cache ships with no business consumer — nothing needs it until live prices arrive — but it stays in the stack and in the readiness check so the topology is real from day one and the later phase adds a client rather than a service. Data-protection key persistence is deferred; it must land alongside the first stored ciphertext, or every new revision orphans it.

---

## Known gaps and deviations

- **The brief lists WebSockets, and that is what the alert feed uses**, through the framework's own real-time library. The reasoning and the comparison with the hand-written alternative belong in the README from the first commit, not deferred to the phase that adds the feed.
- **The identity module's contracts project ships empty**, deliberately, with a note inside saying why. Nothing calls identity at runtime — the token is self-contained — which is the argument that it is the cheapest module to extract.
- **Changing a password is not built.** There is no caller until the settings screen. When it is written, the guard belongs in the mutator, unlike the constructor rule above: the ORM never calls a setter, so a guard there runs only for real callers.

---

## Done when

- `docker compose up` from a clean clone serves the app and reports the database and cache healthy.
- Register, land on the dashboard shell, hard-refresh, still signed in.
- Sign out, hit a protected page, bounce to login with the return path preserved, sign in, come back to where you were.
- A malformed email on registration returns 400 with field-level problem details, from the filter and not from an exception.
- Backend and frontend suites pass, including role isolation, the parameterisation interceptor and the single-refresh count.
- The infrastructure template compiles, a what-if run is clean, and HTTP probes are declared.
- The public site talks to the deployed API; a deep link straight to the login page loads.
- The README records how to run it, the token-storage decision, the role isolation and the transport comparison.
- Usable at 375 pixels wide.

## Reference

These describe the shape of the system rather than the order it gets built in. They live in `docs/reference/`.

- [Identity — sessions and tokens](../reference/identity-contracts.md) — the token rules this phase sets, and what about them is fixed.
- [Data model](../reference/er-diagram.md) — the users and refresh-token tables, and the per-module schema rule.
- [Module boundaries](../reference/module-boundaries.md) — why the layering is the way this phase establishes it.
