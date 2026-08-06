import { useId, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { Alert } from '../components/Alert'
import { Card } from '../components/Card'
import { applyServerLanguage, SUPPORTED_LANGUAGES, type Language } from '../lib/i18n'
import { fallbackMessage, useSaveState, useSyncWhileIdle } from '../lib/useSaveState'
import { appearanceKeys, appearanceQuery } from './appearanceApi'
import { saveAppearance } from './settingsApi'
import { SaveButton } from './SaveButton'

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
  const save = useSaveState()
  useSyncWhileIdle(data?.language as Language | undefined, save.state, setLanguage)

  const mutation = useMutation({
    mutationFn: () => saveAppearance({ theme: data?.theme ?? 'system', language }),
    onSuccess: (result) => {
      queryClient.setQueryData(appearanceKeys.view(), result)
      // Applies the choice immediately rather than waiting for `useSyncServerLanguage` to
      // notice on its next mount — this IS the screen that lets someone change it.
      void applyServerLanguage(language)
      save.succeed()
    },
    onError: (mutationError) => save.fail(fallbackMessage(mutationError, t('common:fallbackError'))),
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
              save.markDirty()
            }}
          >
            {SUPPORTED_LANGUAGES.map((choice) => (
              <option key={choice} value={choice}>
                {t(`language.options.${choice}`)}
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
