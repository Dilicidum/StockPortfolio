import { createElement, StrictMode } from 'react'
import { beforeEach, describe, expect, it } from 'vitest'
import { act, render, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { QueryClientProvider, useQuery } from '@tanstack/react-query'
import { authStore } from '../src/auth/authStore'
import { queryClient } from '../src/lib/queryClient'
import { __resetRefreshInFlight } from '../src/lib/apiClient'
import { alertHistoryQuery, alertKeys, type FiredAlert } from '../src/alerts/alertsApi'
import { useAlertStream } from '../src/alerts/useAlertStream'
import { FakeEventSource } from './fakeEventSource'
import { alertHistoryHandler, firedAlert, notificationOf, streamTicketHandler } from './msw/alerts'
import { server } from './msw/server'

/**
 * The hook on its own, with no router and no page. Everything asserted here is invisible
 * from the DOM — how many connections exist, whether a spent one was closed, how many
 * tickets were asked for — and every one of them is a failure that looks like nothing at
 * all in a browser until the connection budget runs out or alerts quietly stop arriving.
 *
 * `render` rather than `renderHook`, and that is load-bearing rather than a style choice:
 * **`renderHook` with a StrictMode `wrapper` does not double-invoke effects** — measured,
 * not assumed — while `render` of a tree whose root is `<StrictMode>` does. The first test
 * below exists precisely to catch a StrictMode fault, so a harness that quietly switches
 * StrictMode off would have made it a test that cannot fail.
 *
 * `.ts` rather than `.tsx` (the plan names it), so the tree is `createElement`.
 */
beforeEach(() => {
  authStore.signOut()
  queryClient.clear()
  __resetRefreshInFlight()
})

/** Calls the hook and renders nothing. `withHistory` adds the observer some tests need. */
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

const settle = () => new Promise((resolve) => setTimeout(resolve, 80))

/** The connection the hook is holding. Fails loudly rather than returning undefined. */
function latest(): FakeEventSource {
  const source = FakeEventSource.latest()
  if (!source) throw new Error('No EventSource was opened.')
  return source
}

describe('alert stream', () => {
  /*
   * THE STRICTMODE TEST, and it is not a formality. React 19 invokes an effect, tears it
   * down, and invokes it again. The connection is opened AFTER an await — the ticket
   * request — so the first pass reaches `new EventSource` long after its own cleanup has
   * run. Without the `cancelled` flag that second socket is opened by a dead effect and
   * nothing holds a reference to close it: one of the browser's six connections per origin,
   * gone for the life of the tab, in development only, with nothing on screen to show it.
   */
  it('opens exactly one connection under StrictMode, not two', async () => {
    let tickets = 0

    server.use(
      http.post('*/api/alerts/stream-ticket', () => {
        tickets += 1
        return HttpResponse.json({ ticket: `ticket-${tickets}`, expiresAt: '2026-08-06T12:00:30+00:00' })
      }),
    )

    mount()

    await waitFor(() => expect(FakeEventSource.instances).toHaveLength(1))

    // Long enough for the abandoned first pass to have opened its own, if it were going to.
    await act(settle)

    // Two tickets were asked for — that is StrictMode doing its job, and it is what makes
    // this test meaningful. Only one of them became a connection.
    expect(tickets).toBe(2)
    expect(FakeEventSource.instances).toHaveLength(1)
    expect(FakeEventSource.live).toHaveLength(1)
  })

  it('puts a pushed alert at the top of the history cache and ignores the heartbeat', async () => {
    server.use(streamTicketHandler)
    queryClient.setQueryData<FiredAlert[]>(alertKeys.history(), [])

    mount()
    await waitFor(() => expect(FakeEventSource.instances).toHaveLength(1))

    const source = latest()
    const older = firedAlert({ ticker: 'MSFT' })
    const newer = firedAlert({ ticker: 'TSLA', direction: 'Rise', changePercent: '6.43' })

    act(() => {
      source.emitOpen()
      // The 20-second heartbeat. It exists to keep the platform from closing an idle
      // request at four minutes; it is not data, and it must not reach the cache.
      source.emit('ping', { at: '2026-08-06T12:00:20+00:00' })
    })

    expect(queryClient.getQueryData<FiredAlert[]>(alertKeys.history())).toEqual([])

    act(() => {
      source.emit('alert', notificationOf(older))
      source.emit('alert', notificationOf(newer))
    })

    const history = queryClient.getQueryData<FiredAlert[]>(alertKeys.history())

    expect(history?.map((alert) => alert.ticker)).toEqual(['TSLA', 'MSFT'])

    // The pushed payload carries bare price strings and ONE currency; the cache holds the
    // `Money` shape the panel renders. If that conversion is ever dropped the rows render
    // as an em dash, because `formatMoney` cannot read a string.
    expect(history?.[0]?.triggerPrice).toEqual({ amount: '142.0000', currency: 'USD' })
  })

  /*
   * THE RECONNECT, and the reason `EventSource`'s own is unusable. The ticket in the URL is
   * single-use and was spent when this connection was accepted, so the browser's automatic
   * retry — same URL, forever, at whatever interval the server suggested — is a guaranteed
   * 401 loop. Closing the source is what stops it; a second ticket request is what proves
   * the replacement was not the same URL again.
   */
  it('closes a dropped connection, takes a fresh ticket, and refetches history rather than replaying', async () => {
    let tickets = 0
    let historyReads = 0

    server.use(
      http.post('*/api/alerts/stream-ticket', () => {
        tickets += 1
        return HttpResponse.json({ ticket: `ticket-${tickets}`, expiresAt: '2026-08-06T12:00:30+00:00' })
      }),
      http.get('*/api/alerts', () => {
        historyReads += 1
        return HttpResponse.json([])
      }),
    )

    // An observer for the history query, because `invalidateQueries` refetches ACTIVE
    // queries — with nothing watching, the invalidation would be a no-op and this test
    // would pass while the reconnect silently left a hole in the list.
    mount(true)

    await waitFor(() => expect(FakeEventSource.instances).toHaveLength(1))
    await waitFor(() => expect(historyReads).toBeGreaterThan(0))

    // Measured relative to a settled baseline rather than against a literal: StrictMode
    // mounts the observer twice, so the count on arrival here is a harness artefact.
    await act(settle)

    const first = latest()
    act(() => first.emitOpen())

    const ticketsBeforeDrop = tickets
    const readsBeforeDrop = historyReads

    act(() => first.emitError())

    // Immediate, and independent of the backoff: the spent URL must never be retried.
    expect(first.closed).toBe(true)

    await waitFor(() => expect(FakeEventSource.instances).toHaveLength(2), { timeout: 3_000 })

    const second = latest()
    expect(second.url).not.toBe(first.url)
    expect(tickets).toBe(ticketsBeforeDrop + 1)

    // No replay and no backfill: what was missed comes back as an ordinary GET.
    act(() => second.emitOpen())
    await waitFor(() => expect(historyReads).toBe(readsBeforeDrop + 1))
  })

  /*
   * Asserted as a TICKET COUNT, not as a connection count, and the difference is the whole
   * value of the test. `connect()` re-checks the cancelled flag after its await, so a timer
   * that survives the unmount still opens no `EventSource` — the instance count stays at
   * one and a test written against it passes with `clearTimeout` deleted. What actually
   * escapes is the request: a signed-out page asking the API for a stream ticket it will
   * never use, on a backoff that never ends.
   */
  it('cancels a pending reconnect when the layout unmounts', async () => {
    let tickets = 0

    server.use(
      http.post('*/api/alerts/stream-ticket', () => {
        tickets += 1
        return HttpResponse.json({ ticket: `ticket-${tickets}`, expiresAt: '2026-08-06T12:00:30+00:00' })
      }),
      alertHistoryHandler(),
    )

    const { unmount } = mount()
    await waitFor(() => expect(FakeEventSource.instances).toHaveLength(1))

    const source = latest()
    act(() => source.emitError())

    // Unmounting between the drop and the retry is the ordinary case — signing out, or a
    // hot reload.
    unmount()

    expect(source.closed).toBe(true)
    const ticketsAtUnmount = tickets

    await act(() => new Promise((resolve) => setTimeout(resolve, 1_400)))

    expect(tickets).toBe(ticketsAtUnmount)
    expect(FakeEventSource.instances).toHaveLength(1)
  })
})
