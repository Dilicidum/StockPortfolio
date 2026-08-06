import { queryOptions } from '@tanstack/react-query'
import { apiFetch } from '../lib/apiClient'

export interface AppearanceSettings {
  theme: string
  language: string
}

export const APPEARANCE_DEFAULTS: AppearanceSettings = { theme: 'system', language: 'en' }

export const appearanceKeys = {
  all: ['appearance'] as const,
  view: () => [...appearanceKeys.all, 'view'] as const,
}

export const appearanceQuery = queryOptions({
  queryKey: appearanceKeys.view(),
  queryFn: ({ signal }) => apiFetch<AppearanceSettings>('/api/settings/appearance', { signal }),
  staleTime: 300_000,
})
