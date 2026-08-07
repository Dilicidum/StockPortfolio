import { queryOptions } from '@tanstack/react-query'
import { apiFetch } from '../lib/apiClient'
import type { Money } from '../lib/format'

export type AlertDirection = 'Fall' | 'Rise'

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

export interface FiredAlert {
  id: string
  ticker: string
  direction: AlertDirection
  changePercent: string
  endpointPercent: string
  triggerPrice: Money
  referencePrice: Money
  firedAt: string
  isSimulated: boolean
  reason: string
}

export interface AlertNotification {
  id: string
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

export const ALERT_HUB_PATH = '/api/alerts/stream'

export const ALERT_METHOD_NAME = 'AlertFired'

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

export const alertKeys = {
  all: ['alerts'] as const,
  history: () => [...alertKeys.all, 'history'] as const,
  settings: () => [...alertKeys.all, 'settings'] as const,
}

export const ALERT_HISTORY_LIMIT = 50

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

export const simulateAlert = (ticker?: string): Promise<void> =>
  apiFetch<void>('/api/alerts/simulate', { method: 'POST', body: { ticker: ticker ?? null } })
