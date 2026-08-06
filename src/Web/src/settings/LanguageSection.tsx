import { useEffect, useId, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { Alert } from '../components/Alert'
import { Button } from '../components/Button'
import { Card } from '../components/Card'
import { applyServerLanguage, SUPPORTED_LANGUAGES, type Language } from '../lib/i18n'
import { appearanceKeys, appearanceQuery } from './appearanceApi'
import { saveAppearance } from './settingsApi'

type SaveState = 'idle' | 'saving' | 'saved' | 'error'

/**
 * `appearanceQuery` again, not a second GET — the wire shape bundles theme and language
 * together, so this section and `AppearanceSection` share one cache and each writes its own
 * field into the same PUT.
 */
export function LanguageSection() {
  const { t } = useTranslation(['settings', 'common'])
  const { data } = useQuery(appearanceQuery)
  const queryClient = useQueryClient()
  const languageId = useId()

  const [language, setLanguage] = useState<Language>('en')
  const [state, setState] = useState<SaveState>('idle')
  const [error, setError] = useState('')

  useEffect(() => {
    if (data && state === 'idle') setLanguage(data.language as Language)
  }, [data, state])

  const save = useMutation({
    mutationFn: () => saveAppearance({ theme: data?.theme ?? 'system', language }),
    onSuccess: (result) => {
      queryClient.setQueryData(appearanceKeys.view(), result)
      // Applies the choice immediately rather than waiting for `useSyncServerLanguage` to
      // notice on its next mount — this IS the screen that lets someone change it.
      void applyServerLanguage(language)
      setState('saved')
    },
    onError: (mutationError) => {
      setState('error')
      setError(mutationError instanceof Error && mutationError.message ? mutationError.message : t('common:fallbackError'))
    },
  })

  return (
    <Card title={t('language.title')}>
      <div className="flex flex-col gap-3">
        <div className="flex flex-col gap-[7px]">
          <label htmlFor={languageId} className="text-mu text-xs">
            {t('language.label')}
          </label>
          <select
            id={languageId}
            className="border-bd bg-panel text-tx rounded-[9px] border px-[13px] py-[11px] text-sm sm:max-w-[240px]"
            value={language}
            onChange={(event) => {
              setLanguage(event.target.value as Language)
              setState('idle')
            }}
          >
            {SUPPORTED_LANGUAGES.map((choice) => (
              <option key={choice} value={choice}>
                {t(`language.options.${choice}`)}
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
