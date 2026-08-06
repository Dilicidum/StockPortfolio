const REFRESH_TOKEN_KEY = 'stockportfolio.refreshToken'

let accessToken: string | null = null
let accessExpiresAt: string | null = null

export function getAccessToken(): string | null {
  return accessToken
}

export function getAccessExpiresAt(): string | null {
  return accessExpiresAt
}

export function getRefreshToken(): string | null {
  try {
    return globalThis.localStorage?.getItem(REFRESH_TOKEN_KEY) ?? null
  } catch {
    return null
  }
}

export interface TokenPair {
  tokenType?: string
  accessToken: string
  refreshToken?: string | null
  expiresIn: number
}

export function setTokens(pair: TokenPair): void {
  accessToken = pair.accessToken
  accessExpiresAt = new Date(Date.now() + pair.expiresIn * 1000).toISOString()

  try {
    if (pair.refreshToken) {
      globalThis.localStorage?.setItem(REFRESH_TOKEN_KEY, pair.refreshToken)
    }
  } catch {
  }
}

export function clearTokens(): void {
  accessToken = null
  accessExpiresAt = null

  try {
    globalThis.localStorage?.removeItem(REFRESH_TOKEN_KEY)
  } catch {
  }
}

export const REFRESH_TOKEN_STORAGE_KEY = REFRESH_TOKEN_KEY
