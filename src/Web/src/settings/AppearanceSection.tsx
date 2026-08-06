import { useId, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { Alert } from '../components/Alert'
import { Card } from '../components/Card'
import { applyTheme, cacheTheme, type ThemeChoice } from '../lib/theme'
import { fallbackMessage, useSaveState, useSyncWhileIdle } from '../lib/useSaveState'
import { appearanceKeys, appearanceQuery } from './appearanceApi'
import { saveAppearance } from './settingsApi'
import { SaveButton } from './SaveButton'

const THEMES: ThemeChoice[] = ['light', 'dark', 'system']

/**
 * Saves on its own — a rejected API key elsewhere on this screen must not carry a theme
 * change down with it. `PUT` replaces both fields at once, so only `theme` changes here and
 * only `language` changes in `LanguageSection`; each reads the other's value off the same cache.
 *
 * This is a LOCAL, immediate apply for the person editing the control right now — see
 * `useSyncServerTheme` for the app-wide sync that also runs the moment the server answers,
 * whether or not this screen is even open.
 */
export function AppearanceSection() {
  const { t } = useTranslation(['settings', 'common'])
  const { data } = useQuery(appearanceQuery)
  const queryClient = useQueryClient()
  const themeId = useId()

  const [theme, setTheme] = useState<ThemeChoice>('system')
  const save = useSaveState()
  useSyncWhileIdle(data?.theme as ThemeChoice | undefined, save.state, setTheme)

  const mutation = useMutation({
    mutationFn: () => saveAppearance({ theme, language: data?.language ?? 'en' }),
    onSuccess: (result) => {
      queryClient.setQueryData(appearanceKeys.view(), result)
      applyTheme(theme)
      cacheTheme(theme)
      save.succeed()
    },
    onError: (mutationError) => save.fail(fallbackMessage(mutationError, t('common:fallbackError'))),
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
              save.markDirty()
            }}
          >
            {THEMES.map((choice) => (
              <option key={choice} value={choice}>
                {t(`appearance.themeOptions.${choice}`)}
              </option>
            ))}
          </select>
        </div>

        <SaveButton
          state={save.state}
          onClick={() => {
            save.begin()
            mutation.mutate()
          }}
        />

        {save.state === 'error' ? <Alert tone="error">{save.error}</Alert> : null}
      </div>
    </Card>
  )
}
