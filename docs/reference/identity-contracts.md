# Identity — sessions and tokens

The decisions behind signing in: how a session is represented, how it is renewed, how it ends, and which of
those choices must not be changed casually.

## Two tokens, doing different jobs

A session is a pair. The **access token** is short-lived, signed, and carries who you are — so nothing has to
call Identity to check it. The **refresh token** is long-lived and opaque: it carries nothing, means nothing
on its own, and exists only to be exchanged for a new pair.

The refresh token is stored **only as a hash**. The database therefore never holds anything that can be
replayed if it leaks, which is the whole reason the token is opaque rather than signed.

Sessions are held per browser tab: the access token in memory, the refresh token in tab-scoped storage. No
cookies anywhere — the SPA and the API are permanently on different origins, so any cookie would be a
third-party one, and some browsers block those outright.

## Three durations, all still provisional

| | Value | What it controls |
|---|---|---|
| Access token lifetime | 15 minutes | how long a stolen access token is useful |
| Refresh token lifetime | 14 days | how long before you must sign in again |
| Rotation grace period | 30 seconds | how long a just-replaced refresh token keeps working |

**These three numbers are the one thing on this page still awaiting a real decision.** They are plausible
defaults, not researched ones.

## Rotation is unconditional

Every refresh issues a new pair and retires the one presented. There is no setting to turn this off, and
there should not be — a toggle invites callers to branch on it, and a session model that behaves two ways is
two session models.

The grace period exists because two tabs can refresh at the same moment. The loser presents a token that was
retired milliseconds ago, and without a grace window it would be signed out for no reason.

## Revoking and rotating are different endings

Both retire a refresh token. Only rotation names a **successor**.

That distinction is load-bearing, and getting it wrong fails silently. Anything deciding whether a retired
token is still inside its grace window must check for a successor, not merely that the token was retired —
otherwise signing out keeps working for the length of the grace window, the user believes they are signed
out, and every test still passes. A test pins exactly this.

## Sign-in failure says as little as possible

There is one failure answer, not two. Separating "no such account" from "wrong password" tells an attacker
which addresses are registered.

Timing must not leak it either, so when no account matches, the password is still verified — against a
throwaway hash — so the slow hashing runs either way.

## One canonical form for an email address

An address is normalised in exactly one place, and both storing and looking up go through it. A lookup that
normalises differently from what was stored simply misses, and the failure looks like a missing account
rather than a bug.

**"Is this address already taken?" is a question the sign-up flow asks, not something read back out of a
database error.** It is asked *before* hashing the password, because hashing is deliberately slow and a
taken address is a conflict whatever the password was.

The accepted cost: two simultaneous registrations of the same address can both pass the check, and the loser
then hits the unique index and surfaces as a server error rather than a conflict. The index stays — it is
what keeps the data correct — and the window is a millisecond wide. Reintroduce special handling only if
that error is ever actually observed.

## The public surface

```
POST /api/auth/register   {email, password}  -> 201 {accessToken, refreshToken, accessExpiresAt} | 409 | 400
POST /api/auth/login      {email, password}  -> 200 same shape | 401
POST /api/auth/refresh    {refreshToken}     -> 200 same shape | 401
POST /api/auth/logout     bearer token       -> 204
GET  /api/auth/me         bearer token       -> 200 {id, email}
```

Every route also declares the framework-generated failures it can emit, which is truthful only because the
host is configured to give those bare statuses a body.

The user's identity is read from the token's subject claim. The framework will helpfully rename that claim
to a long legacy URI unless told not to, at which point looking it up by its real name silently returns
nothing — so the renaming is switched off in the host.

---

**Where the unbuilt parts come from.** The token lifetimes here are still provisional. Changing a password has no home yet; [Phase 5](../plan/phase-5-make-it-mine.md) is where a settings screen would need one.
