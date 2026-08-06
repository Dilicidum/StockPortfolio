import { queryOptions } from '@tanstack/react-query'
import { apiFetch } from '../lib/apiClient'
import type { AppearanceSettings } from './appearanceApi'

/**
 * The API contract, verbatim:
 *
 *   GET  /api/settings/appearance   bearer   -> 200 { theme, language }
 *   PUT  /api/settings/appearance   bearer   -> 200 { theme, language }
 *   GET  /api/settings/dashboard    bearer   -> 200 { refreshIntervalSeconds }
 *   PUT  /api/settings/dashboard    bearer   -> 200 { refreshIntervalSeconds }
 *   GET  /api/settings/api-key      bearer   -> 200 { configured, lastFour, rejected }
 *   POST /api/settings/api-key      bearer   -> 200 { configured, lastFour, rejected }
 *                                              -> 400 (the provider rejected the key)
 *                                              -> 503 (the provider could not answer)
 *                                              -> 404 (bring-your-own-key is switched off)
 *   DELETE /api/settings/api-key    bearer   -> 204
 *
 * Appearance's GET already lives in `appearanceApi.ts` (Task 1 needed it before this screen
 * existed, to sync the language before first paint) — its type and query are reused here
 * rather than redeclared. Everything else this screen needs is new.
 */

export interface DashboardSettings {
  refreshIntervalSeconds: number
}

/** `lastFour` is null until a key is saved; the key itself never appears in any response. */
export interface ApiKeyStatus {
  configured: boolean
  lastFour: string | null
  rejected: boolean
}

/** Query keys live beside the fetchers for their feature, exactly as `alertKeys` does. */
export const settingsKeys = {
  all: ['settings'] as const,
  dashboard: () => [...settingsKeys.all, 'dashboard'] as const,
  apiKey: () => [...settingsKeys.all, 'apiKey'] as const,
}

export const saveAppearance = (body: AppearanceSettings): Promise<AppearanceSettings> =>
  apiFetch<AppearanceSettings>('/api/settings/appearance', { method: 'PUT', body })

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
