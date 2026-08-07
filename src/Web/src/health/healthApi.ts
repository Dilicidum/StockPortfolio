import { queryOptions } from '@tanstack/react-query'
import { apiFetch } from '../lib/apiClient'

export type HealthState = 'Healthy' | 'Degraded' | 'Unhealthy'

export interface HealthComponent {
  name: string
  status: HealthState
  description: string | null
  durationMs: number
  data: Record<string, unknown>
}

export interface HealthReport {
  status: HealthState
  totalDurationMs: number
  components: HealthComponent[]
}

export interface FeedFacts {
  provider: string | null
  providerKeyRejected: boolean
  lastCycleAt: string | null
  tickersTargeted: number | null
}

export const REDIS_COMPONENT = 'redis'
export const FEED_COMPONENT = 'marketdata-feed'
export const DATABASE_COMPONENT_PREFIX = 'postgres-'

export const HEALTH_REFETCH_MS = 30_000

export const healthKeys = {
  all: ['health'] as const,
  detail: () => [...healthKeys.all, 'detail'] as const,
}

export const healthDetailQuery = queryOptions({
  queryKey: healthKeys.detail(),
  queryFn: ({ signal }) => apiFetch<HealthReport>('/api/health/detail', { signal }),
  refetchInterval: HEALTH_REFETCH_MS,
  staleTime: HEALTH_REFETCH_MS,
})

const SEVERITY: readonly HealthState[] = ['Healthy', 'Degraded', 'Unhealthy']

function worst(states: readonly HealthState[]): HealthState | null {
  if (states.length === 0) return null

  return states.reduce((worstSoFar, state) =>
    SEVERITY.indexOf(state) > SEVERITY.indexOf(worstSoFar) ? state : worstSoFar,
  )
}

export function componentStatus(report: HealthReport | undefined, name: string): HealthState | null {
  return report?.components.find((component) => component.name === name)?.status ?? null
}

export function databaseStatus(report: HealthReport | undefined): HealthState | null {
  return worst(
    (report?.components ?? [])
      .filter((component) => component.name.startsWith(DATABASE_COMPONENT_PREFIX))
      .map((component) => component.status),
  )
}

export function feedFacts(report: HealthReport | undefined): FeedFacts {
  const data = report?.components.find((component) => component.name === FEED_COMPONENT)?.data ?? {}

  return {
    provider: typeof data.provider === 'string' ? data.provider : null,
    providerKeyRejected: data.providerKeyRejected === true,
    lastCycleAt: typeof data.lastCycleAt === 'string' ? data.lastCycleAt : null,
    tickersTargeted: typeof data.tickersTargeted === 'number' ? data.tickersTargeted : null,
  }
}

export function alertsSuppressed(report: HealthReport | undefined): boolean {
  const cache = componentStatus(report, REDIS_COMPONENT)

  return cache !== null && cache !== 'Healthy'
}
