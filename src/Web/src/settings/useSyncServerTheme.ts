import { useEffect, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { applyTheme, cacheTheme, isThemeChoice, readCachedTheme, watchSystemTheme, type ThemeChoice } from '../lib/theme'
import { appearanceQuery } from './appearanceApi'

/**
 * THE SERVER WINS for theme, exactly as `useSyncServerLanguage` does for language — see that
 * file for why the pre-sign-in cache is only ever a guess for the render before this
 * resolves. Without this, a second device with empty browser storage painted from the OS
 * preference while the Settings dropdown correctly read the saved choice: the control and
 * the page disagreeing for the whole session.
 *
 * This hook also carries the live half of "Match system": `lib/theme.ts`'s `watchSystemTheme`
 * already existed, with a correct teardown and unit tests, but nothing ever called it, so an
 * OS theme flip mid-session only ever took effect the next time someone opened Settings and
 * pressed Save. `choice` tracks whichever value is currently in force — the cache before the
 * server has answered, the server's value after — and the listener attaches only while that
 * value is 'system' and is torn down the moment it stops being 'system', including on
 * unmount (React 19 StrictMode mounts this effect twice, so the teardown has to actually run).
 *
 * Mounted once, from `_authenticated.tsx`, beside `useSyncServerLanguage`.
 */
export function useSyncServerTheme(): void {
  const { data } = useQuery(appearanceQuery)
  const [choice, setChoice] = useState<ThemeChoice>(() => readCachedTheme())

  useEffect(() => {
    if (data && isThemeChoice(data.theme)) {
      applyTheme(data.theme)
      cacheTheme(data.theme)
      setChoice(data.theme)
    }
  }, [data])

  useEffect(() => {
    if (choice !== 'system') return undefined

    return watchSystemTheme(() => applyTheme('system'))
  }, [choice])
}
