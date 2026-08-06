import { createElement, StrictMode } from 'react'
import { beforeEach, describe, expect, it } from 'vitest'
import { act, render, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { QueryClientProvider, useQuery } from '@tanstack/react-query'
import { authStore } from '../src/auth/authStore'
import { queryClient } from '../src/lib/queryClient'
import { __resetRefreshInFlight } from '../src/lib/apiClient'
import { setTokens } from '../src/lib/tokenStore'
import {
  alertHistoryQuery,
  alertKeys,
  ALERT_METHOD_NAME,
  type FiredAlert,
} from '../src/alerts/alertsApi'
import { useAlertStream } from '../src/alerts/useAlertStream'
import { FakeHubConnection } from './fakeHubConnection'
import { alertHistoryHandler, firedAlert, notificationOf } from './msw/alerts'
import { server } from './msw/server'

/**
 * The hook on its own, with no router and no page. SignalR itself is faked — see
 * tests/fakeHubConnection.ts — because asserting on its frames would be testing Microsoft's
 * code. What is left is entirely ours: which transport we asked for, what a pushed alert does
 * to the cache, what a recovered connection does about the gap, and how the token is renewed.
 *
 * `render` rather than `renderHook`, and that is load-bearing: `renderHook` with a StrictMode
 * `wrapper` does not double-invoke effects — measured, not assumed — while `render` of a tree
 * whose root is `<StrictMode>` does.
 */
beforeEach(() => {
  authStore.signOut()
  queryClient.clear()
  __resetRefreshInFlight()
})

function Probe({ withHistory = false }: { withHistory?: boolean }) {
  useAlertStream()
  // Hook order is stable because `withHistory` never changes for a given mount.
  if (withHistory) useQuery(alertHistoryQuery)
  return null
}

function mount(withHistory = false) {
  return render(
    createElement(
      StrictMode,
      null,
      createElement(QueryClientProvider, { client: queryClient }, createElement(Probe, { withHistory })),
    ),
  )
}

/** The connection the hook is holding. Fails loudly rather than returning undefined. */
function latest(): FakeHubConnection {
  const connection = FakeHubConnection.latest()
  if (!connection) throw new Error('No hub connection was built.')
  return connection
}

describe('alert stream', () => {
  /*
   * THE TRANSPORT TEST, and it is not a formality. WebSockets-only plus skipping negotiation is
   * the documented exemption from sticky sessions with a Redis backplane, and it needs BOTH
   * halves. Allowing a fallback transport, or letting negotiation run, means a browser can be
   * routed to a replica that knows nothing about its connection — which shows up as alerts that
   * arrive for some users and not others, only in production, only with more than one replica.
   */
  it('asks for WebSockets only and skips negotiation', async () => {
    mount()

    await waitFor(() => expect(FakeHubConnection.latest()).not.toBeNull())

    const { options } = latest()

    expect(options.transport).toBe(1)
    expect(options.skipNegotiation).toBe(true)
  })

  it('puts a pushed alert at the top of the history cache', async () => {
    queryClient.setQueryData<FiredAlert[]>(alertKeys.history(), [])

    mount()
    await waitFor(() => expect(FakeHubConnection.latest()).not.toBeNull())

    const connection = latest()
    const older = firedAlert({ ticker: 'MSFT' })
    const newer = firedAlert({ ticker: 'TSLA', direction: 'Rise', changePercent: '6.43' })

    act(() => {
      connection.push(ALERT_METHOD_NAME, notificationOf(older))
      connection.push(ALERT_METHOD_NAME, notificationOf(newer))
    })

    const history = queryClient.getQueryData<FiredAlert[]>(alertKeys.history())

    expect(history?.map((alert) => alert.ticker)).toEqual(['TSLA', 'MSFT'])

    // The pushed payload carries bare price strings and ONE currency; the cache holds the
    // `Money` shape the panel renders. If that conversion is ever dropped the rows render as an
    // em dash, because `formatMoney` cannot read a string.
    expect(history?.[0]?.triggerPrice).toEqual({ amount: '142.0000', currency: 'USD' })
  })

  /*
   * NO REPLAY, honoured on the client. The connection only ever pushes new alerts, so anything
   * that fired while it was down is a hole by definition. SignalR reconnects on its own now;
   * what is still ours is closing that hole with an ordinary refetch instead of a backfill.
   */
  it('refetches history when a dropped connection comes back, rather than replaying', async () => {
    let historyReads = 0

    server.use(
      http.get('*/api/alerts', () => {
        historyReads += 1
        return HttpResponse.json([])
      }),
    )

    // An observer for the history query, because `invalidateQueries` refetches ACTIVE queries —
    // with nothing watching, the invalidation would be a no-op and this test would pass while
    // the reconnect silently left a hole in the list.
    mount(true)

    await waitFor(() => expect(FakeHubConnection.latest()).not.toBeNull())
    await waitFor(() => expect(historyReads).toBeGreaterThan(0))

    // Measured against a settled baseline rather than a literal: StrictMode mounts the observer
    // twice, so the count on arrival here is a harness artefact.
    await act(() => new Promise((resolve) => setTimeout(resolve, 80)))

    const readsBeforeDrop = historyReads

    act(() => latest().dropAndRecover())

    await waitFor(() => expect(historyReads).toBe(readsBeforeDrop + 1))
  })

  /*
   * THE TOKEN. SignalR calls this before every request it makes, including each reconnect
   * attempt — so handing back whatever is in memory means a reconnect after a 15-minute outage
   * presents an expired token and 401s for ever, on a schedule that never gives up. Asserted as
   * "it went and got a new one", because that is the failure: not a wrong token, a stale one.
   */
  it('renews an expired access token before reconnecting', async () => {
    let refreshes = 0

    server.use(
      http.post('*/api/auth/refresh', () => {
        refreshes += 1
        return HttpResponse.json({ accessToken: 'renewed', refreshToken: 'next', expiresIn: 900 })
      }),
      alertHistoryHandler(),
    )

    // In date, and far enough out that the renewal window does not catch it.
    setTokens({ accessToken: 'still-good', refreshToken: 'r', expiresIn: 900 })

    mount()
    await waitFor(() => expect(FakeHubConnection.latest()).not.toBeNull())

    const factory = latest().options.accessTokenFactory
    expect(factory).toBeDefined()

    expect(await factory!()).toBe('still-good')
    expect(refreshes).toBe(0)

    // Now expired. `expiresIn` is seconds, and a negative one puts the instant in the past.
    setTokens({ accessToken: 'stale', refreshToken: 'r', expiresIn: -1 })

    expect(await factory!()).toBe('renewed')
    expect(refreshes).toBe(1)
  })
})
