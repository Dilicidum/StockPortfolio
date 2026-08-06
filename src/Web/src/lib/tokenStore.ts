/**
 * SESSION MODEL — read this before changing anything here.
 *
 * ONE SESSION PER BROWSER, not per tab. Signed in is signed in everywhere;
 * signed out is signed out everywhere, in every tab, immediately.
 *
 * The refresh token lives in localStorage. The access token lives in a
 * MODULE-SCOPED VARIABLE and nowhere else — it is short-lived and there is no
 * reason to write it down, so each tab mints its own from the shared refresh
 * token.
 *
 * WHY NOT AN httpOnly COOKIE. It is the right answer and it is unavailable
 * here. The SPA is served from github.io and the API from Azure Container
 * Apps, so a cookie the API sets is third-party: Safari blocks those outright,
 * Firefox partitions them, Chrome restricts them. A credential that silently
 * does not exist for some visitors is worse than one in storage. If the SPA is
 * ever served from the same origin as the API, a cookie becomes possible and
 * this whole file should go.
 *
 * WHY NOT sessionStorage, WHICH IS WHAT THIS USED TO BE. sessionStorage is
 * scoped to one tab, so tabs could not share a session. That was papered over
 * with a BroadcastChannel that let a tab with no credential ask the others and
 * adopt whatever it was handed — so a brand-new tab signed itself in silently,
 * and signing out in one tab left the others signed in. Both were surprising,
 * and neither is how anything else on the web behaves. localStorage makes the
 * sharing real instead of simulated, and the message bus is deleted rather
 * than fixed.
 *
 * THE COST, stated plainly: a 14-day refresh token is now written to disk,
 * where script injected into this origin can read it in one line, and the
 * session survives closing the browser. That is the ordinary trade every SPA
 * without a first-party cookie makes. Note what it does NOT change — a live
 * XSS could already call the API with the in-memory bearer and could already
 * call refresh itself, so this widens the window rather than opening a door.
 *
 * THERE IS NO COOKIE, in any deployment. The server sets none anywhere; every
 * auth endpoint returns the pair in the body. An earlier version of this
 * comment described a dual-mode design where compose used a cookie and Pages
 * used storage. None of the cookie half was ever built, and the description
 * survived for months because it read like a settled decision. If a cookie is
 * ever added, change this comment in the same commit.
 */

const REFRESH_TOKEN_KEY = 'stockportfolio.refreshToken'

let accessToken: string | null = null
let accessExpiresAt: string | null = null

/** In-memory only. Returns null when this tab has not minted one yet. */
export function getAccessToken(): string | null {
  return accessToken
}

export function getAccessExpiresAt(): string | null {
  return accessExpiresAt
}

/**
 * Always read through to storage rather than caching in memory. Another tab may
 * have rotated the token since this one last looked, and refresh tokens are
 * single-use — a stale copy is one the server has already retired.
 *
 * localStorage is absent in some SSR/test environments — never let it throw.
 */
export function getRefreshToken(): string | null {
  try {
    return globalThis.localStorage?.getItem(REFRESH_TOKEN_KEY) ?? null
  } catch {
    return null
  }
}

export interface TokenPair {
  accessToken: string
  refreshToken?: string | null
  accessExpiresAt: string
}

export function setTokens(pair: TokenPair): void {
  accessToken = pair.accessToken
  accessExpiresAt = pair.accessExpiresAt

  try {
    if (pair.refreshToken) {
      globalThis.localStorage?.setItem(REFRESH_TOKEN_KEY, pair.refreshToken)
    }
  } catch {
    // Private-mode Safari and friends. A failed write costs this tab its
    // session on the next reload; the current one keeps working.
  }
}

/**
 * Ends the session for the whole browser. Removing the key fires a `storage`
 * event in every OTHER tab — never in this one — which auth/sessionSync.ts
 * turns into an immediate sign-out there. That is the entire cross-tab logout
 * mechanism, and it is the browser's, not ours.
 */
export function clearTokens(): void {
  accessToken = null
  accessExpiresAt = null

  try {
    globalThis.localStorage?.removeItem(REFRESH_TOKEN_KEY)
  } catch {
    // ignore
  }
}

/** The key other modules listen for. Exported so nothing has to repeat it. */
export const REFRESH_TOKEN_STORAGE_KEY = REFRESH_TOKEN_KEY
