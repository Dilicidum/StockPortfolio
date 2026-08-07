import { beforeEach, expect, it } from 'vitest'
import { delay, http, HttpResponse } from 'msw'
import { __resetRefreshInFlight, apiFetch } from '../src/lib/apiClient'
import { clearTokens, setTokens } from '../src/lib/tokenStore'
import { server } from './msw/server'

beforeEach(() => {
  clearTokens()
  __resetRefreshInFlight()
})

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
      await delay(40)
      return HttpResponse.json({
        accessToken: 'fresh-token',
        refreshToken: 'rotated-refresh-token',
        expiresIn: 900,
      })
    }),
  )

  setTokens({
    accessToken: 'stale-token',
    refreshToken: 'original-refresh-token',
    expiresIn: -1,
  })

  const CONCURRENCY = 10
  const results = await Promise.all(
    Array.from({ length: CONCURRENCY }, () => apiFetch<{ email: string }>('/api/auth/me')),
  )

  expect(refreshCalls).toBe(1)

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
        expiresIn: 900,
      })
    }),
  )

  setTokens({ accessToken: 'stale-token', refreshToken: 'r', expiresIn: 0 })
  await apiFetch('/api/auth/me')
  expect(refreshCalls).toBe(1)

  setTokens({ accessToken: 'stale-token', refreshToken: 'r', expiresIn: 0 })
  await apiFetch('/api/auth/me')
  expect(refreshCalls).toBe(2)
})
