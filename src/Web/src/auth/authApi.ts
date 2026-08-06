import { apiFetch, refreshAccessToken } from '../lib/apiClient'
import { setTokens, type TokenPair } from '../lib/tokenStore'
import { authStore, type AuthUser } from './authStore'

export interface Credentials {
  email: string
  password: string
}

/**
 * The API contract, verbatim:
 *
 *   POST /api/auth/register  {email,password} -> 201 TokenPair | 409 | 400
 *   POST /api/auth/login     {email,password} -> 200 TokenPair | 401
 *   POST /api/auth/refresh   {refreshToken}   -> 200 TokenPair | 401
 *   POST /api/auth/logout    bearer           -> 204
 *   GET  /api/auth/me        bearer           -> 200 {id,email}
 *
 * Errors are application/problem+json; a 400 carries field-level `errors`.
 */

export const authKeys = {
  me: ['auth', 'me'] as const,
}

export function fetchMe(signal?: AbortSignal): Promise<AuthUser> {
  return apiFetch<AuthUser>('/api/auth/me', { signal })
}

/**
 * Login and register hand back tokens but not the user, so both are a two-step:
 * store the tokens (GET /me needs the bearer), then ask who we are, and only
 * then flip the store to authenticated. Doing it in that order means the app is
 * never in the state "holds a token, has no identity" — which the route guard
 * would read as signed in while the shell rendered an empty user chip.
 */
async function completeSignIn(tokens: TokenPair): Promise<AuthUser> {
  setTokens(tokens)

  try {
    const user = await fetchMe()
    authStore.setUser(user)
    return user
  } catch (error) {
    authStore.signOut()
    throw error
  }
}

export async function login(credentials: Credentials): Promise<AuthUser> {
  const tokens = await apiFetch<TokenPair>('/api/auth/login', {
    method: 'POST',
    body: credentials,
    authenticated: false,
  })
  return completeSignIn(tokens)
}

export async function register(credentials: Credentials): Promise<AuthUser> {
  const tokens = await apiFetch<TokenPair>('/api/auth/register', {
    method: 'POST',
    body: credentials,
    authenticated: false,
  })
  return completeSignIn(tokens)
}

export async function logout(): Promise<void> {
  try {
    await apiFetch<void>('/api/auth/logout', { method: 'POST' })
  } finally {
    // The local session goes whether or not the server call lands. A network
    // blip must not leave the user staring at a dashboard they just left.
    authStore.signOut()
  }
}

/**
 * Restores a session on page load: swap the refresh token — always from
 * localStorage, there is no cookie in any deployment, see lib/tokenStore.ts —
 * for a fresh access token, then identify the user. `refreshAccessToken` has
 * already stored the pair by the time this returns.
 *
 * Rejection here is the ordinary "not signed in" path, not an error worth
 * reporting — a first-time visitor takes it on every load.
 */
export async function restoreSession(): Promise<AuthUser> {
  await refreshAccessToken()

  try {
    const user = await fetchMe()
    authStore.setUser(user)
    return user
  } catch (error) {
    authStore.signOut()
    throw error
  }
}
