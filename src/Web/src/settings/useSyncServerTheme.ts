import { useEffect, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { applyTheme, cacheTheme, isThemeChoice, readCachedTheme, watchSystemTheme, type ThemeChoice } from '../lib/theme'
import { appearanceQuery } from './appearanceApi'

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
