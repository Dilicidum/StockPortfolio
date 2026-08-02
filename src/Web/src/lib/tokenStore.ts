/**
 * SESSION MODEL — read this before changing anything here.
 *
 * The access token lives in a MODULE-SCOPED VARIABLE and nowhere else. Not
 * localStorage, not sessionStorage, not a non-httpOnly cookie. A short-lived
 * bearer sitting in web storage is readable by any script that gets injected
 * into the origin; keeping it in a closure means an XSS has to be live and
 * resident to steal it rather than just walking storage once.
 *
 * The refresh token goes to sessionStorage, in every deployment.
 *
 * THERE IS NO COOKIE. This comment used to describe a dual-mode design — an
 * httpOnly cookie under compose, sessionStorage under Pages, with the client
 * blind to which — and none of the cookie half was ever built. The server sets
 * no cookie anywhere (`grep -ri "response.cookies\|httponly\|samesite"` over
 * the backend returns nothing); every auth endpoint returns the pair in the
 * body. The description survived because it read like a settled decision, so
 * nobody re-derived it. If a cookie is ever added, change this comment in the
 * same commit.
 *
 * sessionStorage, not localStorage: it is scoped to the tab and dies when the
 * tab closes, so a shared machine does not leak a live session into the next
 * person's browser window, and an XSS cannot walk storage once and leave with
 * a 14-day credential. An httpOnly cookie would be stronger still, and is
 * unavailable: the SPA is on github.io and the API on Azure Container Apps, so
 * the cookie would be third-party, and Safari blocks those outright.
 *
 * Being tab-scoped is also why a second tab used to land on /login while the
 * first was still signed in. That is fixed by handing the session between tabs
 * over BroadcastChannel rather than by storing it somewhere shared — see
 * auth/sessionChannel.ts.
 */

const REFRESH_TOKEN_KEY = 'stockportfolio.refreshToken'

let accessToken: string | null = null
let accessExpiresAt: string | null = null

/** In-memory only. Returns null when there is no live session. */
export function getAccessToken(): string | null {
  return accessToken
}

export function getAccessExpiresAt(): string | null {
  return accessExpiresAt
}

/** sessionStorage is absent in some SSR/test environments — never let it throw. */
export function getRefreshToken(): string | null {
  try {
    return globalThis.sessionStorage?.getItem(REFRESH_TOKEN_KEY) ?? null
  } catch {
    return null
  }
}

export interface TokenPair {
  accessToken: string
  /** Absent under compose, where the refresh token is an httpOnly cookie. */
  refreshToken?: string | null
  accessExpiresAt: string
}

/**
 * Notified after every local token change, so auth/sessionChannel.ts can mirror
 * it to the other tabs. A callback rather than an import because this module
 * must not depend on the channel: the channel already depends on this one, and
 * a cycle between them breaks module initialisation order in ways that only
 * show up in the bundled build.
 */
let tokensChanged: ((pair: TokenPair | null) => void) | null = null

export function setTokensChangedListener(
  listener: ((pair: TokenPair | null) => void) | null,
): void {
  tokensChanged = listener
}

export function setTokens(pair: TokenPair): void {
  accessToken = pair.accessToken
  accessExpiresAt = pair.accessExpiresAt

  try {
    if (pair.refreshToken) {
      globalThis.sessionStorage?.setItem(REFRESH_TOKEN_KEY, pair.refreshToken)
    }
  } catch {
    // Private-mode Safari and friends. A failed write only costs us the
    // cross-origin refresh path; the cookie path is unaffected.
  }

  tokensChanged?.(pair)
}

export function clearTokens(): void {
  accessToken = null
  accessExpiresAt = null

  try {
    globalThis.sessionStorage?.removeItem(REFRESH_TOKEN_KEY)
  } catch {
    // ignore
  }

  tokensChanged?.(null)
}
