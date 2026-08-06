import { http, HttpResponse } from 'msw'
import type { AlertNotification, AlertSetting, FiredAlert } from '../../src/alerts/alertsApi'

/**
 * `_authenticated` opens the alert stream and the dashboard mounts the panel, so EVERY test
 * that merely mounts a protected route now makes three alert requests — history, settings
 * and a stream ticket — before it asserts anything. `tests/setup.ts` runs MSW with
 * `onUnhandledRequest: 'error'`, so these are not optional extras for the alerts tests;
 * they are what keeps the dashboard and portfolio suites from failing on a request neither
 * of them cares about.
 */

/** A signed-in account with no thresholds and nothing fired — the quiet default. */
export const alertHistoryHandler = (alerts: FiredAlert[] = []) =>
  http.get('*/api/alerts', () => HttpResponse.json(alerts))

export const alertSettingsHandler = (settings: AlertSetting[] = []) =>
  http.get('*/api/alerts/settings', () => HttpResponse.json(settings))

/**
 * Single-use server-side, which is exactly why the hook never lets `EventSource` retry on
 * its own. Nothing here enforces that — a handler that counted redemptions would be testing
 * the mock — so the reconnect test asserts on the number of TICKETS asked for instead.
 */
export const streamTicketHandler = http.post('*/api/alerts/stream-ticket', () =>
  HttpResponse.json({ ticket: 'ticket-1', expiresAt: '2026-08-06T12:00:30+00:00' }),
)

export const alertsHandlers = [alertHistoryHandler(), alertSettingsHandler(), streamTicketHandler]

let sequence = 0

/** A fired alert with sane defaults. Overrides are spread last so any field can be pinned. */
export function firedAlert(overrides: Partial<FiredAlert> = {}): FiredAlert {
  sequence += 1

  return {
    id: `0199a1f0-0000-7000-8000-00000000${String(sequence).padStart(4, '0')}`,
    ticker: 'AAPL',
    direction: 'Fall',
    changePercent: '-5.33',
    endpointPercent: '-2.07',
    triggerPrice: { amount: '142.0000', currency: 'USD' },
    referencePrice: { amount: '150.0000', currency: 'USD' },
    firedAt: '2026-08-06T12:00:00+00:00',
    isSimulated: false,
    reason: 'fell 5.33% from the window high',
    ...overrides,
  }
}

/**
 * The pushed shape, which is NOT the history shape: prices are bare strings beside one
 * shared `currency`. Building it from a `FiredAlert` here is what makes the stream tests
 * assert against the same row the panel would have rendered from the wire.
 */
export function notificationOf(alert: FiredAlert): AlertNotification {
  return {
    id: alert.id,
    userId: '0199a1f0-0000-7000-8000-0000000000ff',
    ticker: alert.ticker,
    direction: alert.direction,
    changePercent: alert.changePercent,
    endpointPercent: alert.endpointPercent,
    triggerPrice: alert.triggerPrice.amount,
    referencePrice: alert.referencePrice.amount,
    currency: alert.triggerPrice.currency,
    firedAt: alert.firedAt,
    isSimulated: alert.isSimulated,
    reason: alert.reason,
  }
}
