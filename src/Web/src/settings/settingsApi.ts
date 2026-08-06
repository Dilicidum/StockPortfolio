import { queryOptions } from '@tanstack/react-query'
import { apiFetch } from '../lib/apiClient'
import { APPEARANCE_DEFAULTS, type AppearanceSettings } from './appearanceApi'

export interface DashboardSettings {
  refreshIntervalSeconds: number
}

export const REFRESH_INTERVAL_SECONDS = [15, 30, 60, 120, 300] as const

export const DEFAULT_REFRESH_SECONDS = 60

export interface ApiKeyStatus {
  configured: boolean
  lastFour: string | null
  rejected: boolean
}

export const settingsKeys = {
  all: ['settings'] as const,
  dashboard: () => [...settingsKeys.all, 'dashboard'] as const,
  apiKey: () => [...settingsKeys.all, 'apiKey'] as const,
}

export const saveAppearance = (body: AppearanceSettings): Promise<AppearanceSettings> =>
  apiFetch<AppearanceSettings>('/api/settings/appearance', { method: 'PUT', body })

export const saveAppearancePatch = (
  current: AppearanceSettings | undefined,
  patch: Partial<AppearanceSettings>,
): Promise<AppearanceSettings> => saveAppearance({ ...APPEARANCE_DEFAULTS, ...current, ...patch })

export const dashboardSettingsQuery = queryOptions({
  queryKey: settingsKeys.dashboard(),
  queryFn: ({ signal }) => apiFetch<DashboardSettings>('/api/settings/dashboard', { signal }),
})

export const saveDashboardSettings = (body: DashboardSettings): Promise<DashboardSettings> =>
  apiFetch<DashboardSettings>('/api/settings/dashboard', { method: 'PUT', body })

export const apiKeyStatusQuery = queryOptions({
  queryKey: settingsKeys.apiKey(),
  queryFn: ({ signal }) => apiFetch<ApiKeyStatus>('/api/settings/api-key', { signal }),
})

export const saveApiKey = (apiKey: string): Promise<ApiKeyStatus> =>
  apiFetch<ApiKeyStatus>('/api/settings/api-key', { method: 'POST', body: { apiKey } })

export const removeApiKey = (): Promise<void> =>
  apiFetch<void>('/api/settings/api-key', { method: 'DELETE' })
