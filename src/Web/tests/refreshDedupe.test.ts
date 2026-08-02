import { beforeEach, expect, it } from 'vitest'
import { delay, http, HttpResponse } from 'msw'
import { __resetRefreshInFlight, apiFetch } from '../src/lib/apiClient'
import { clearTokens, setTokens } from '../src/lib/tokenStore'
import { server } from './msw/server'

beforeEach(() => {
  clearTokens()
  __resetRefreshInFlight()
})

/**
 * The failure this prevents:
 *
 * A dashboard fires several queries at once. The access token has just expired,
 * so every one of them comes back 401 in the same tick. If each 401 triggers
 * its own POST /api/auth/refresh, the server rotates the refresh token N times
 * and N-1 of the responses carry a token that was already superseded before it
 * arrived. The user is logged out at random, only under concurrency, only
 * sometimes — and it reproduces on nobody's machine.
 *
 * The fix is a single shared in-flight promise. This test is the proof, and it
 * counts requests rather than inspecting internals so it keeps working if the
 * implementation of the dedupe changes.
 */
it('collapses concurrent 401s into exactly one refresh call', async () => {
  let refreshCalls = 0
  let meCalls = 0

  server.use(
    http.get('*/api/auth/me', ({ request }) => {
      meCalls += 1
      if (request.headers.get('authorization') !== 'Bearer fresh-token') {
        return HttpResponse.json(
          { title: 'Unauthorized', status: 401 },
          { status: 401, headers: { 'Content-Type': 'application/problem+json' } },
        )
      }
      return HttpResponse.json({ id: 'u-1', email: 'holder@example.com' })
    }),

    http.post('*/api/auth/refresh', async () => {
      refreshCalls += 1
      // A real refresh is a network round trip. The delay keeps every 401 inside
      // the in-flight window, which is what makes this deterministic rather
      // than a race that happens to pass.
      await delay(40)
      return HttpResponse.json({
        accessToken: 'fresh-token',
        refreshToken: 'rotated-refresh-token',
        accessExpiresAt: new Date(Date.now() + 900_000).toISOString(),
      })
    }),
  )

  setTokens({
    accessToken: 'stale-token',
    refreshToken: 'original-refresh-token',
    accessExpiresAt: new Date(Date.now() - 1_000).toISOString(),
  })

  const CONCURRENCY = 10
  const results = await Promise.all(
    Array.from({ length: CONCURRENCY }, () => apiFetch<{ email: string }>('/api/auth/me')),
  )

  expect(refreshCalls).toBe(1)

  // Every caller still got its answer: one 401 and one retry each.
  expect(results).toHaveLength(CONCURRENCY)
  for (const result of results) {
    expect(result.email).toBe('holder@example.com')
  }
  expect(meCalls).toBe(CONCURRENCY * 2)
})

it('starts a new refresh once the previous one has settled', async () => {
  let refreshCalls = 0

  server.use(
    http.get('*/api/auth/me', ({ request }) =>
      request.headers.get('authorization') === 'Bearer fresh-token'
        ? HttpResponse.json({ id: 'u-1', email: 'holder@example.com' })
        : HttpResponse.json({ status: 401 }, { status: 401 }),
    ),
    http.post('*/api/auth/refresh', () => {
      refreshCalls += 1
      return HttpResponse.json({
        accessToken: 'fresh-token',
        refreshToken: 'rotated',
        accessExpiresAt: new Date(Date.now() + 900_000).toISOString(),
      })
    }),
  )

  setTokens({ accessToken: 'stale-token', refreshToken: 'r', accessExpiresAt: '' })
  await apiFetch('/api/auth/me')
  expect(refreshCalls).toBe(1)

  // The slot must be released when the promise settles, or the second expiry
  // would reuse a resolved promise holding a token that is now stale too.
  setTokens({ accessToken: 'stale-token', refreshToken: 'r', accessExpiresAt: '' })
  await apiFetch('/api/auth/me')
  expect(refreshCalls).toBe(2)
})
