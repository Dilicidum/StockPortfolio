import { queryOptions } from '@tanstack/react-query'
import { API_BASE_URL, apiFetch } from '../lib/apiClient'
import type { Money } from '../lib/format'

/**
 * The API contract, verbatim:
 *
 *   GET  /api/alerts/settings       bearer                    -> 200 AlertSetting[]
 *   PUT  /api/alerts/settings       SaveAlertSettingBody      -> 200 AlertSetting
 *                                                             -> 409 TickerNotHeld
 *                                                             -> 409 WindowExceedsRetention
 *   GET  /api/alerts?limit=50       bearer                    -> 200 FiredAlert[]
 *   POST /api/alerts/stream-ticket  bearer, no body           -> 200 StreamTicket
 *   GET  /api/alerts/stream?ticket= the ticket IS the auth    -> text/event-stream
 *   POST /api/alerts/simulate       {ticker?}                 -> 202 | 409
 *
 * TWO SHAPES FOR ONE ROW, and the difference is not cosmetic. `GET /api/alerts` answers
 * with `FiredAlert`, whose prices are `Money` objects like every other price in the app.
 * The stream pushes `AlertNotification`, whose prices are bare strings beside a single
 * shared `currency`. Both are written down in the phase plan and they are not the same
 * record, so `toFiredAlert` converts one into the other in exactly one place — the stream
 * prepends into the history cache, and a cache holding two shapes renders two ways.
 */

/** Serialised by `JsonStringEnumConverter`, so it arrives as the enum's name. */
export type AlertDirection = 'Fall' | 'Rise'

/**
 * A threshold on one position. `thresholdPercent` is a NUMBER, not a string: it is a
 * value the user typed rather than a figure the server computed, the plan declares it
 * `decimal` with no converter, and `NumberHandling.Strict` on the host would reject a
 * quoted one on the way back in. Every *computed* percentage below is still a string.
 */
export interface AlertSetting {
  ticker: string
  thresholdPercent: number
  windowMinutes: number
  enabled: boolean
}

export interface SaveAlertSettingBody {
  ticker: string
  thresholdPercent: number
  windowMinutes: number
  enabled: boolean
}

/** One row of history, and what the panel and the notifications screen both render. */
export interface FiredAlert {
  id: string
  ticker: string
  direction: AlertDirection
  /** The extreme move, signed and pre-formatted, e.g. "-5.33". The client appends the `%`. */
  changePercent: string
  /** The endpoint move it was checked against — the sign-agreement rule's other half. */
  endpointPercent: string
  triggerPrice: Money
  /** The window extreme the move was measured from. */
  referencePrice: Money
  firedAt: string
  isSimulated: boolean
  /** Server-written, e.g. "fell 5.33% from the window high". Names the comparison. */
  reason: string
}

/** The `alert` event's payload. Money travels as strings here too, one currency for both prices. */
export interface AlertNotification {
  id: string
  /** Present on the pub/sub payload; the stream is already per-user, so nothing reads it. */
  userId?: string
  ticker: string
  direction: AlertDirection
  changePercent: string
  endpointPercent: string
  triggerPrice: string
  referencePrice: string
  currency: string
  firedAt: string
  isSimulated: boolean
  reason: string
}

export interface StreamTicket {
  ticket: string
  expiresAt: string
}

export function toFiredAlert(notification: AlertNotification): FiredAlert {
  const { currency } = notification

  return {
    id: notification.id,
    ticker: notification.ticker,
    direction: notification.direction,
    changePercent: notification.changePercent,
    endpointPercent: notification.endpointPercent,
    triggerPrice: { amount: notification.triggerPrice, currency },
    referencePrice: { amount: notification.referencePrice, currency },
    firedAt: notification.firedAt,
    isSimulated: notification.isSimulated,
    reason: notification.reason,
  }
}

/** Query keys live beside the fetchers for their feature, exactly as `holdingKeys` does. */
export const alertKeys = {
  all: ['alerts'] as const,
  history: () => [...alertKeys.all, 'history'] as const,
  settings: () => [...alertKeys.all, 'settings'] as const,
}

/**
 * The server's own `Alerts:HistoryLimit` ceiling, and it is fetched ONCE for both views.
 * The panel shows the newest few and the notifications screen shows the lot, off one key —
 * so the stream has one cache to prepend into. Two keys would mean a pushed alert appearing
 * in whichever view happened to be mounted and missing from the other.
 */
export const ALERT_HISTORY_LIMIT = 50

/** How many of them the dashboard panel shows before "See all". */
export const PANEL_ROWS = 6

export const alertHistoryQuery = queryOptions({
  queryKey: alertKeys.history(),
  queryFn: ({ signal }) =>
    apiFetch<FiredAlert[]>(`/api/alerts?limit=${ALERT_HISTORY_LIMIT}`, { signal }),
})

export const alertSettingsQuery = queryOptions({
  queryKey: alertKeys.settings(),
  queryFn: ({ signal }) => apiFetch<AlertSetting[]>('/api/alerts/settings', { signal }),
})

export const saveAlertSetting = (body: SaveAlertSettingBody): Promise<AlertSetting> =>
  apiFetch<AlertSetting>('/api/alerts/settings', { method: 'PUT', body })

/**
 * The manual trigger the brief asks for. It goes through the real path server-side —
 * saved, then published — so what arrives proves the mechanism rather than the button.
 * An omitted ticker lets the server pick one of the caller's positions.
 */
export const simulateAlert = (ticker?: string): Promise<void> =>
  apiFetch<void>('/api/alerts/simulate', { method: 'POST', body: { ticker: ticker ?? null } })

/** No body: a ticket request has no input, and `logout` already POSTs the same way. */
export const createStreamTicket = (): Promise<StreamTicket> =>
  apiFetch<StreamTicket>('/api/alerts/stream-ticket', { method: 'POST' })

/**
 * Absolute, because `EventSource` is not `apiFetch` and knows nothing about `API_BASE_URL`.
 * The ticket is in the query string because the browser cannot put a header on this kind of
 * connection — see `useAlertStream` for the rest of that argument.
 */
export const alertStreamUrl = (ticket: string): string =>
  `${API_BASE_URL}/api/alerts/stream?ticket=${encodeURIComponent(ticket)}`
