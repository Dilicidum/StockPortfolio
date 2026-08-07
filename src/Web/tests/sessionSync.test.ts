import { afterEach, beforeEach, expect, it } from 'vitest'
import { http, HttpResponse } from 'msw'
import { authStore } from '../src/auth/authStore'
import { startSessionSync } from '../src/auth/sessionSync'
import { __resetRefreshInFlight } from '../src/lib/apiClient'
import { queryClient } from '../src/lib/queryClient'
import { clearTokens, getRefreshToken, setTokens } from '../src/lib/tokenStore'
import { server } from './msw/server'

const KEY = 'stockportfolio.refreshToken'

let stop: (() => void) | null = null

function otherTabWrote(newValue: string | null): void {
  if (newValue === null) globalThis.localStorage.removeItem(KEY)
  else globalThis.localStorage.setItem(KEY, newValue)

  globalThis.dispatchEvent(
    new StorageEvent('storage', {
      key: KEY,
      newValue,
      storageArea: globalThis.localStorage,
    }),
  )
}

const settle = () => new Promise((resolve) => setTimeout(resolve, 10))

beforeEach(() => {
  clearTokens()
  authStore.signOut()
  __resetRefreshInFlight()
  queryClient.clear()
  stop = startSessionSync()
})

afterEach(() => {
  stop?.()
  stop = null
  clearTokens()
  authStore.signOut()
})

it('puts the refresh token where every tab can see it, not just this one', () => {
  setTokens({ accessToken: 'a', refreshToken: 'shared-refresh', expiresIn: 900 })

  expect(globalThis.localStorage.getItem(KEY)).toBe('shared-refresh')
  expect(globalThis.sessionStorage.getItem(KEY)).toBeNull()
  expect(getRefreshToken()).toBe('shared-refresh')
})

it('signs this tab out the moment another tab signs out', async () => {
  setTokens({ accessToken: 'a', refreshToken: 'shared-refresh', expiresIn: 900 })
  authStore.setUser({ id: 'u-1', email: 'holder@example.com' })

  expect(authStore.getState().isAuthenticated).toBe(true)

  otherTabWrote(null)
  await settle()

  expect(authStore.getState().isAuthenticated).toBe(false)
  expect(authStore.getState().user).toBeNull()
})

it('signs this tab in when a session appears in another tab', async () => {
  server.use(
    http.post('*/api/auth/refresh', () =>
      HttpResponse.json({
        accessToken: 'minted-here',
        refreshToken: 'shared-refresh',
        expiresIn: 900,
      }),
    ),
    http.get('*/api/auth/me', () => HttpResponse.json({ id: 'u-1', email: 'holder@example.com' })),
  )

  expect(authStore.getState().isAuthenticated).toBe(false)

  otherTabWrote('shared-refresh')
  await settle()

  expect(authStore.getState().isAuthenticated).toBe(true)
  expect(authStore.getState().user?.email).toBe('holder@example.com')
})

it('ignores a rotation in another tab rather than racing it for the token', async () => {
  let refreshCalls = 0

  server.use(
    http.post('*/api/auth/refresh', () => {
      refreshCalls += 1
      return HttpResponse.json({
        accessToken: 'x',
        refreshToken: 'y',
        expiresIn: 900,
      })
    }),
  )

  setTokens({ accessToken: 'a', refreshToken: 'first-token', expiresIn: 900 })
  authStore.setUser({ id: 'u-1', email: 'holder@example.com' })

  otherTabWrote('rotated-by-the-other-tab')
  await settle()

  expect(refreshCalls).toBe(0)
  expect(authStore.getState().isAuthenticated).toBe(true)
  expect(getRefreshToken()).toBe('rotated-by-the-other-tab')
})
