import { queryOptions } from '@tanstack/react-query'
import { apiFetch } from '../lib/apiClient'

/**
 * The API contract, verbatim:
 *
 *   GET /api/settings/appearance   bearer   -> 200 { theme, language }
 *
 * Only `language` is consumed here, by `useSyncServerLanguage`. The settings SCREEN — the
 * read/write pair, the theme half, the form — is a later task; this file exists early
 * because the server's language has to win over the pre-sign-in cache (see `lib/i18n.ts`)
 * the moment a session exists, and that cannot wait for the screen that lets someone change it.
 */
export interface AppearanceSettings {
  theme: string
  language: string
}

/** Query keys live beside the fetcher for their feature, exactly as `holdingKeys` does. */
export const appearanceKeys = {
  all: ['appearance'] as const,
  view: () => [...appearanceKeys.all, 'view'] as const,
}

export const appearanceQuery = queryOptions({
  queryKey: appearanceKeys.view(),
  queryFn: ({ signal }) => apiFetch<AppearanceSettings>('/api/settings/appearance', { signal }),
  // The value changes only when someone visits a settings screen that does not exist yet;
  // there is nothing to poll for.
  staleTime: 300_000,
})
