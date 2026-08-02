/**
 * SESSION MODEL — read this before changing anything here.
 *
 * The access token lives in a MODULE-SCOPED VARIABLE and nowhere else. Not
 * localStorage, not sessionStorage, not a non-httpOnly cookie. A short-lived
 * bearer sitting in web storage is readable by any script that gets injected
 * into the origin; keeping it in a closure means an XSS has to be live and
 * resident to steal it rather than just walking storage once.
 *
 * The refresh token is the awkward one, because where it lives depends on how
 * the app is deployed:
 *
 *   docker compose (the P0 gate)
 *     nginx serves the SPA and proxies /api to the API, so browser and API
 *     share an origin. The API sets the refresh token as an httpOnly, SameSite
 *     cookie. JavaScript cannot read it, which is the strong option. Every
 *     request here goes out with `credentials: 'include'`.
 *
 *   GitHub Pages
 *     The SPA is on github.io and the API is on Azure Container Apps. Different
 *     sites, so a refresh cookie would have to be SameSite=None and third-party
 *     — which current browsers block by default. The API therefore also returns
 *     the refresh token in the response body and we hold it in sessionStorage.
 *
 * sessionStorage, not localStorage: it is scoped to the tab and dies when the
 * tab closes, so a shared machine does not leak a live session into the next
 * person's browser window. That is a genuine weakening versus the httpOnly
 * cookie and the honest cost of static hosting; the README says so, and it is
 * the argument for a short refresh TTL.
 *
 * The client does not branch on deployment. It always sends the body token if
 * it has one AND always sends credentials, so whichever mechanism the server
 * actually used is the one that works. Under compose the body value is
 * redundant; under Pages the cookie is absent. Neither case needs a flag.
 */

const REFRESH_TOKEN_KEY = 'tz.refreshToken'

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
}

export function clearTokens(): void {
  accessToken = null
  accessExpiresAt = null

  try {
    globalThis.sessionStorage?.removeItem(REFRESH_TOKEN_KEY)
  } catch {
    // ignore
  }
}
