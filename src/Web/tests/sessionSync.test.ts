import { afterEach, beforeEach, expect, it } from 'vitest'
import { http, HttpResponse } from 'msw'
import { authStore } from '../src/auth/authStore'
import { startSessionSync } from '../src/auth/sessionSync'
import { __resetRefreshInFlight } from '../src/lib/apiClient'
import { queryClient } from '../src/lib/queryClient'
import { clearTokens, getRefreshToken, setTokens } from '../src/lib/tokenStore'
import { server } from './msw/server'

/**
 * The failures these prevent:
 *
 * The session used to live in sessionStorage, which is scoped to one tab, and
 * the gap was papered over with a BroadcastChannel that let a tab holding no
 * credential ask the others for one. Two things fell out of that, and both are
 * what these tests now pin the opposite of. A brand-new tab signed itself in
 * with no credential of its own, which reads as the app having no auth at all.
 * And signing out stayed in the tab it happened in, so the other tabs kept
 * working until their access token happened to expire.
 *
 * One session per browser fixes both, and the `storage` event does the work —
 * it fires in every tab except the one that made the change.
 */

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

/** Long enough for the listener's own promise chain, short enough to stay a unit test. */
const settle = () => new Promise((resolve) => setTimeout(resolve, 10))

beforeEach(() => {
  clearTokens()
  authStore.signOut()
  __resetRefreshInFlight()
  // Not hygiene — load-bearing. The client's default staleTime is 30s, so a
  // cached identity from the previous test is served without a network call,
  // and the request counter below silently counts zero for the wrong reason.
  // Leaving this out made the rotation test pass against an implementation
  // that did exactly what it exists to forbid.
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
  // The whole point. If this ever goes back to sessionStorage, a second tab has
  // no credential again and something will be invented to hand it one.
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

/**
 * The one that catches the obvious wrong implementation.
 *
 * "Something changed, so restore the session" passes the two tests above and is
 * still wrong: refresh tokens are single-use, so an already-signed-in tab that
 * reacts to someone else's rotation spends a token that tab is still using, and
 * one of them is logged out as soon as the 30-second grace window closes. It
 * fails intermittently, under two tabs, on nobody's machine.
 *
 * Counting requests is what catches it — every assertion about auth state
 * passes either way, because the tab genuinely is still signed in afterwards.
 */
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
  // This tab reads storage at the moment it refreshes, so it picks the new one
  // up on its own — which is why there is nothing to do here.
  expect(getRefreshToken()).toBe('rotated-by-the-other-tab')
})
