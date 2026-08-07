import { http, HttpResponse } from 'msw'
import type { AlertNotification, AlertSetting, FiredAlert } from '../../src/alerts/alertsApi'

export const alertHistoryHandler = (alerts: FiredAlert[] = []) =>
  http.get('*/api/alerts', () => HttpResponse.json(alerts))

export const alertSettingsHandler = (settings: AlertSetting[] = []) =>
  http.get('*/api/alerts/settings', () => HttpResponse.json(settings))

export const alertsHandlers = [alertHistoryHandler(), alertSettingsHandler()]

let sequence = 0

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
