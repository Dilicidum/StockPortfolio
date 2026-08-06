import { useEffect } from 'react'
import { useQuery } from '@tanstack/react-query'
import { applyServerLanguage, isSupportedLanguage } from '../lib/i18n'
import { appearanceQuery } from './appearanceApi'

/**
 * THE SERVER WINS, the moment it has answered. `lib/i18n.ts`'s cached language is only ever
 * a guess for the render before this resolves — a returning user on a fresh browser, or a
 * browser whose cache disagrees with what they actually chose last time, would otherwise see
 * one language flash to the other only on THEIR SECOND visit, which is exactly the kind of
 * bug that survives every test that reloads once.
 *
 * Mounted once, from `_authenticated.tsx`, the same place that opens the alert stream —
 * nothing before sign-in has a session to ask, and GET /api/settings/appearance would 401.
 */
export function useSyncServerLanguage(): void {
  const { data } = useQuery(appearanceQuery)

  useEffect(() => {
    if (data && isSupportedLanguage(data.language)) {
      applyServerLanguage(data.language)
    }
  }, [data])
}
