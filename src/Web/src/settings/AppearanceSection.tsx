import { useEffect, useId, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { Alert } from '../components/Alert'
import { Button } from '../components/Button'
import { Card } from '../components/Card'
import { applyTheme, cacheTheme, type ThemeChoice } from '../lib/theme'
import { appearanceKeys, appearanceQuery } from './appearanceApi'
import { saveAppearance } from './settingsApi'

type SaveState = 'idle' | 'saving' | 'saved' | 'error'

const THEMES: ThemeChoice[] = ['light', 'dark', 'system']

/**
 * Saves on its own — a rejected API key elsewhere on this screen must not carry a theme
 * change down with it. `PUT` replaces both fields at once, so only `theme` changes here and
 * only `language` changes in `LanguageSection`; each reads the other's value off the same cache.
 */
export function AppearanceSection() {
  const { t } = useTranslation(['settings', 'common'])
  const { data } = useQuery(appearanceQuery)
  const queryClient = useQueryClient()
  const themeId = useId()

  const [theme, setTheme] = useState<ThemeChoice>('system')
  const [state, setState] = useState<SaveState>('idle')
  const [error, setError] = useState('')

  // Synced once the server answers, only while nothing is mid-edit — a background refetch
  // overwriting an unsaved choice would look like the page ignoring a click.
  useEffect(() => {
    if (data && state === 'idle') setTheme(data.theme as ThemeChoice)
  }, [data, state])

  const save = useMutation({
    mutationFn: () => saveAppearance({ theme, language: data?.language ?? 'en' }),
    onSuccess: (result) => {
      queryClient.setQueryData(appearanceKeys.view(), result)
      applyTheme(theme)
      cacheTheme(theme)
      setState('saved')
    },
    onError: (mutationError) => {
      setState('error')
      setError(mutationError instanceof Error && mutationError.message ? mutationError.message : t('common:fallbackError'))
    },
  })

  return (
    <Card title={t('appearance.title')}>
      <div className="flex flex-col gap-3">
        <div className="flex flex-col gap-[7px]">
          <label htmlFor={themeId} className="text-mu text-xs">
            {t('appearance.themeLabel')}
          </label>
          <select
            id={themeId}
            className="border-bd bg-panel text-tx rounded-[9px] border px-[13px] py-[11px] text-sm sm:max-w-[240px]"
            value={theme}
            onChange={(event) => {
              setTheme(event.target.value as ThemeChoice)
              setState('idle')
            }}
          >
            {THEMES.map((choice) => (
              <option key={choice} value={choice}>
                {t(`appearance.themeOptions.${choice}`)}
              </option>
            ))}
          </select>
        </div>

        <div className="flex flex-wrap items-center gap-3">
          <Button
            size="sm"
            className="sm:max-w-[140px]"
            onClick={() => {
              setState('saving')
              save.mutate()
            }}
            disabled={state === 'saving'}
            loading={state === 'saving'}
          >
            {state === 'saving' ? t('common:actions.saving') : t('common:actions.save')}
          </Button>
          {state === 'saved' ? <span role="status" className="text-up text-[12.5px]">{t('common:actions.saved')}</span> : null}
        </div>

        {state === 'error' ? <Alert tone="error">{error}</Alert> : null}
      </div>
    </Card>
  )
}
