import { useEffect, useSyncExternalStore } from 'react'
import { useQueryClient, type QueryClient } from '@tanstack/react-query'
import {
  ALERT_HISTORY_LIMIT,
  alertKeys,
  alertStreamUrl,
  createStreamTicket,
  toFiredAlert,
  type AlertNotification,
  type FiredAlert,
} from './alertsApi'

/**
 * ─────────────────────────────────────────────────────────────────────────────
 * ONE CONNECTION FOR THE WHOLE APPLICATION.
 * ─────────────────────────────────────────────────────────────────────────────
 *
 * `useAlertStream()` is called in exactly one place — `routes/_authenticated.tsx`,
 * the layout every protected page sits under — and never from a component. A held-open
 * stream permanently occupies one of the browser's six connections per origin, so a
 * per-component hook on a page with three panels would spend half the budget on one page.
 *
 * THREE THINGS THIS FILE EXISTS TO GET RIGHT, each of which fails silently:
 *
 * 1. React 19 StrictMode invokes every effect twice. Without the `cancelled` flag, the
 *    first invocation's in-flight ticket request resolves AFTER its own cleanup has run
 *    and opens a second connection that nothing will ever close. `clearTimeout` in the
 *    cleanup is the same hazard one step later: a pending reconnect timer outlives the
 *    unmount and reconnects a stream nobody is listening to.
 *
 * 2. `EventSource`'s built-in reconnect is UNUSABLE HERE and must be defeated. It retries
 *    the same URL, and that URL carries a single-use ticket which was spent the moment the
 *    first connection was accepted — so every automatic retry is a guaranteed 401, forever,
 *    at whatever interval the server suggested. On `error` we therefore `close()` (which is
 *    what stops the browser retrying), fetch a FRESH ticket, and open a new connection with
 *    backoff. Losing free reconnection is the price of header-less authentication.
 *
 * 3. There is no replay. The stream only ever pushes new alerts, so a reconnection has a
 *    hole in it by definition — every alert that fired while the socket was down. That hole
 *    is closed by invalidating the history query on the way back up, which is an ordinary
 *    refetch of an ordinary `GET`. No cursor, no `Last-Event-ID`, no backfill.
 */

export type AlertStreamStatus = 'connecting' | 'live' | 'reconnecting' | 'offline'

/**
 * An external store rather than context, for the same reason `authStore` is one: the badge
 * that reads it lives in `AppShell`, which is rendered by each route's own component, and
 * threading a provider through the layout to reach it buys nothing a module singleton does
 * not already give. `useSyncExternalStore` needs a stable snapshot, so this is a bare
 * string rather than an object.
 */
let status: AlertStreamStatus = 'connecting'
const listeners = new Set<() => void>()

function setStatus(next: AlertStreamStatus): void {
  if (status === next) return
  status = next
  for (const listener of listeners) listener()
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}

const getStatus = (): AlertStreamStatus => status

export function useAlertStreamStatus(): AlertStreamStatus {
  return useSyncExternalStore(subscribe, getStatus, getStatus)
}

/**
 * The invariant made structural. StrictMode's double invocation is sequential — effect,
 * cleanup, effect — so this flag is only ever true for one live connection at a time, and
 * a second *call site* would find it set and refuse rather than quietly doubling up.
 */
let connected = false

/** Test seam. Never call this from application code. */
export function __resetAlertStream(): void {
  connected = false
  setStatus('connecting')
}

/** 1s, 2s, 5s, 10s, then 30s forever. The last entry is what a long outage settles on. */
const BACKOFF_MS = [1_000, 2_000, 5_000, 10_000, 30_000]

const delayFor = (attempt: number): number =>
  BACKOFF_MS[Math.min(attempt, BACKOFF_MS.length - 1)] ?? 30_000

/**
 * Prepends a pushed alert into the history cache.
 *
 * Deliberately a no-op when the query holds nothing yet: `setQueryData` stamps
 * `dataUpdatedAt`, so seeding an unfetched query would make it look fresh for the whole
 * `staleTime` and the panel would show one pushed row and no history at all. The row is
 * already saved server-side, so the fetch that is about to happen brings it anyway.
 */
function prepend(queryClient: QueryClient, alert: FiredAlert): void {
  queryClient.setQueryData<FiredAlert[]>(alertKeys.history(), (old) => {
    if (old === undefined) return old

    // The same alert can arrive twice — pushed, then again in a refetch that overlapped it.
    return [alert, ...old.filter((row) => row.id !== alert.id)].slice(0, ALERT_HISTORY_LIMIT)
  })
}

export function useAlertStream(): AlertStreamStatus {
  const queryClient = useQueryClient()

  useEffect(() => {
    if (connected) return undefined
    connected = true

    let cancelled = false
    let source: EventSource | null = null
    let timer: ReturnType<typeof setTimeout> | undefined
    let attempt = 0
    let dropped = false

    /** The caller sets the status first, because "the socket dropped" and "no ticket" differ. */
    function scheduleReconnect(): void {
      if (cancelled) return

      timer = setTimeout(() => {
        void connect()
      }, delayFor(attempt))
      attempt += 1
    }

    async function connect(): Promise<void> {
      let ticket: string

      try {
        ticket = (await createStreamTicket()).ticket
      } catch {
        // The ticket endpoint is bearer-authenticated, so this is also the signed-out path.
        // Backing off is right either way: `apiFetch` has already tried a refresh.
        if (!cancelled) {
          setStatus('offline')
          scheduleReconnect()
        }
        return
      }

      // The await above is where StrictMode's first pass gets to after being torn down.
      if (cancelled) return

      const next = new EventSource(alertStreamUrl(ticket))
      source = next

      next.onopen = () => {
        attempt = 0
        setStatus('live')

        // The no-replay decision, honoured on the client: whatever fired while we were
        // down comes back as an ordinary refetch rather than as a replayed event.
        if (dropped) {
          dropped = false
          void queryClient.invalidateQueries({ queryKey: alertKeys.history() })
        }
      }

      next.addEventListener('alert', (event) => {
        try {
          const notification = JSON.parse((event as MessageEvent<string>).data) as AlertNotification
          prepend(queryClient, toFiredAlert(notification))
        } catch {
          // A payload we cannot parse is one alert lost, not a reason to drop the stream.
        }
      })

      // `ping` needs no listener at all. A named event is delivered only to listeners for
      // that name, so an unhandled one is already ignored — and it must be, because it is
      // the 20-second heartbeat against the platform's 4-minute idle close, not data.

      next.onerror = () => {
        // `close()` is the whole point: without it the browser retries this URL by itself,
        // with a ticket that has already been spent, forever.
        next.close()
        if (source === next) source = null

        dropped = true
        setStatus('reconnecting')
        scheduleReconnect()
      }
    }

    setStatus('connecting')
    void connect()

    return () => {
      cancelled = true
      connected = false
      clearTimeout(timer)
      source?.close()
      source = null
    }
  }, [queryClient])

  return useAlertStreamStatus()
}
