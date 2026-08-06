import { useEffect } from 'react'
import { useQuery } from '@tanstack/react-query'
import { applyServerLanguage, isSupportedLanguage } from '../lib/i18n'
import { appearanceQuery } from './appearanceApi'

export function useSyncServerLanguage(): void {
  const { data } = useQuery(appearanceQuery)

  useEffect(() => {
    if (data && isSupportedLanguage(data.language)) {
      void applyServerLanguage(data.language)
    }
  }, [data])
}
