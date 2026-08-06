import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { Alert } from '../components/Alert'
import { Button } from '../components/Button'
import { Card } from '../components/Card'
import { TextField } from '../components/TextField'
import { ApiError } from '../lib/apiClient'
import { fallbackMessage, useSaveState } from '../lib/useSaveState'
import { apiKeyStatusQuery, removeApiKey, saveApiKey, settingsKeys, type ApiKeyStatus } from './settingsApi'
import { SaveButton } from './SaveButton'

/** 400 and 503 stay different sentences — a refused key is not the same failure as a provider
 * that merely could not answer, and conflating them is the `c: 0` mistake all over again. */
function messageFor(error: unknown, t: (key: string) => string): string {
  if (error instanceof ApiError) {
    if (error.status === 400) return t('apiKey.rejectedMessage')
    if (error.status === 503) return t('apiKey.unavailableMessage')
    if (error.status === 404) return t('apiKey.disabledMessage')
  }
  return fallbackMessage(error, t('common:fallbackError'))
}

export function ApiKeySection() {
  const { t } = useTranslation(['settings', 'common'])
  const { data, isError } = useQuery(apiKeyStatusQuery)
  const queryClient = useQueryClient()
  const [apiKey, setApiKey] = useState('')
  const save = useSaveState()

  const saveMutation = useMutation({
    mutationFn: () => saveApiKey(apiKey),
    onSuccess: (result: ApiKeyStatus) => {
      queryClient.setQueryData(settingsKeys.apiKey(), result)
      setApiKey('')
      save.succeed()
    },
    onError: (mutationError) => save.fail(messageFor(mutationError, t)),
  })
  const remove = useMutation({
    mutationFn: removeApiKey,
    onSuccess: () => {
      queryClient.setQueryData<ApiKeyStatus>(settingsKeys.apiKey(), { configured: false, lastFour: null, rejected: false })
      save.markDirty()
    },
    onError: (mutationError) => save.fail(messageFor(mutationError, t)),
  })

  // GET /api/settings/api-key returns 404 when bring-your-own-key is switched off on this
  // deployment. Without this check `data` merely stays undefined, `needsInput` reads as
  // true, and a working-looking form renders for a feature that is not there — the person
  // only finds out after typing a key and pressing Save.
  if (isError) {
    return (
      <Card title={t('apiKey.title')}>
        <p className="text-mu text-[12.5px]">{t('apiKey.disabledMessage')}</p>
      </Card>
    )
  }

  // Reappears whenever no working key is on file: none was ever saved, or the one saved was refused.
  const needsInput = !data?.configured || data.rejected

  return (
    <Card title={t('apiKey.title')}>
      <div className="flex flex-col gap-3">
        <p className="text-mu text-[11.5px] leading-relaxed">{t('apiKey.description')}</p>
        {data?.configured ? (
          <div className="flex flex-wrap items-center gap-3">
            <span className="text-tx text-[12.5px]">{t('apiKey.configuredStatus', { lastFour: data.lastFour })}</span>
            <Button size="sm" variant="secondary" onClick={() => remove.mutate()} loading={remove.isPending}>
              {t('apiKey.remove')}
            </Button>
          </div>
        ) : null}
        {data?.rejected ? <Alert tone="error">{t('apiKey.rejectedNotice')}</Alert> : null}
        {needsInput ? (
          <div className="flex flex-col gap-3">
            <TextField
              label={t('apiKey.inputLabel')}
              type="password"
              autoComplete="off"
              value={apiKey}
              onChange={(event) => {
                setApiKey(event.target.value)
                save.markDirty()
              }}
            />
            <SaveButton
              state={save.state}
              label={t('apiKey.save')}
              disabled={apiKey.trim().length === 0}
              onClick={() => {
                save.begin()
                saveMutation.mutate()
              }}
            />
          </div>
        ) : null}

        {save.state === 'error' ? <Alert tone="error">{save.error}</Alert> : null}
      </div>
    </Card>
  )
}
