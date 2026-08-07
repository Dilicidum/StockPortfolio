import { apiFetch, refreshAccessToken } from '../lib/apiClient'
import { setTokens, type TokenPair } from '../lib/tokenStore'
import { authStore, type AuthUser } from './authStore'

export interface Credentials {
  email: string
  password: string
}

export const authKeys = {
  me: ['auth', 'me'] as const,
}

export function fetchMe(signal?: AbortSignal): Promise<AuthUser> {
  return apiFetch<AuthUser>('/api/auth/me', { signal })
}

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
    authStore.signOut()
  }
}

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
