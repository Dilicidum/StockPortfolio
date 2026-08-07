import { http, HttpResponse } from 'msw'
import type { HealthComponent, HealthReport, HealthState } from '../../src/health/healthApi'

export const FAKE_PROVIDER = 'FakeQuoteProvider'

const DATABASE_COMPONENTS = [
  'postgres-identity',
  'postgres-portfolio',
  'postgres-alerts',
  'postgres-marketdata',
] as const

function component(
  name: string,
  status: HealthState,
  data: Record<string, unknown> = {},
): HealthComponent {
  return { name, status, description: null, durationMs: 1.1, data }
}

export interface HealthOverrides {
  databases?: HealthState
  redis?: HealthState
  feed?: HealthState
  provider?: string
  providerKeyRejected?: boolean
  lastCycleAt?: string | null
  tickersTargeted?: number
}

export function healthReport(overrides: HealthOverrides = {}): HealthReport {
  const {
    databases = 'Healthy',
    redis = 'Healthy',
    feed = 'Healthy',
    provider = FAKE_PROVIDER,
    providerKeyRejected = false,
    lastCycleAt = '2026-08-06T12:00:00.0000000+00:00',
    tickersTargeted = 0,
  } = overrides

  const feedData: Record<string, unknown> = {
    tickersTargeted,
    tickersStored: tickersTargeted,
    provider,
    providerKeyRejected,
  }

  if (lastCycleAt !== null) feedData.lastCycleAt = lastCycleAt

  const components = [
    ...DATABASE_COMPONENTS.map((name) => component(name, databases)),
    component('redis', redis),
    component('marketdata-feed', feed, feedData),
  ]

  const worst = components.some((entry) => entry.status === 'Unhealthy')
    ? 'Unhealthy'
    : components.some((entry) => entry.status === 'Degraded')
      ? 'Degraded'
      : 'Healthy'

  return { status: worst, totalDurationMs: 8.4, components }
}

export const healthDetailHandler = (overrides: HealthOverrides = {}) =>
  http.get('*/api/health/detail', () => HttpResponse.json(healthReport(overrides)))
