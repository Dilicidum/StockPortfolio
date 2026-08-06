import { queryOptions } from '@tanstack/react-query'
import { API_BASE_URL, apiFetch } from '../lib/apiClient'
import type { Money } from '../lib/format'

// FiredAlert (GET /api/alerts) and AlertNotification (the stream) are different shapes — toFiredAlert() converts between them in one place.

// Serialised by JsonStringEnumConverter — arrives as the enum's name.
export type AlertDirection = 'Fall' | 'Rise'

// thresholdPercent is a number (user-typed), not a string like the computed percentages below.
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

// One row of alert history — rendered by both the panel and the notifications screen.
export interface FiredAlert {
  id: string
  ticker: string
  direction: AlertDirection
  // Signed and pre-formatted, e.g. "-5.33"; the client appends the %.
  changePercent: string
  // The endpoint move it was checked against — the sign-agreement rule's other half.
  endpointPercent: string
  triggerPrice: Money
  // The window extreme the move was measured from.
  referencePrice: Money
  firedAt: string
  isSimulated: boolean
  // Server-written, e.g. "fell 5.33% from the window high".
  reason: string
}

// The alert event's payload; money travels as strings here, one currency for both prices.
export interface AlertNotification {
  id: string
  // Present on the pub/sub payload; the stream is already per-user, so nothing reads it.
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

// Query keys live beside the fetchers for their feature, exactly as holdingKeys does.
export const alertKeys = {
  all: ['alerts'] as const,
  history: () => [...alertKeys.all, 'history'] as const,
  settings: () => [...alertKeys.all, 'settings'] as const,
}

// Server's Alerts:HistoryLimit ceiling, fetched once so panel and notifications share one cache for the stream to prepend into.
export const ALERT_HISTORY_LIMIT = 50

// How many of them the dashboard panel shows before "See all".
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
