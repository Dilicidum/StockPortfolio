import { useState } from 'react'
import { useMutation, useQueryClient, type QueryKey } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { fallbackMessage } from '../lib/formErrors'
import { useSaveState, useSyncWhileIdle, type SaveStateApi } from '../lib/useSaveState'

export interface SavedSetting<TValue> {
  value: TValue
  change: (value: TValue) => void
  save: SaveStateApi
  submit: () => void
}

export interface SavedSettingOptions<TValue, TResult> {
  serverValue: TValue | undefined
  fallback: TValue
  queryKey: QueryKey
  mutationFn: (value: TValue) => Promise<TResult>
  onSaved?: (value: TValue) => void
}

export function useSavedSetting<TValue, TResult>({
  serverValue,
  fallback,
  queryKey,
  mutationFn,
  onSaved,
}: SavedSettingOptions<TValue, TResult>): SavedSetting<TValue> {
  const { t } = useTranslation('common')
  const queryClient = useQueryClient()
  const [value, setValue] = useState<TValue>(fallback)
  const save = useSaveState()
  useSyncWhileIdle(serverValue, save.state, setValue)

  const mutation = useMutation({
    mutationFn: () => mutationFn(value),
    onSuccess: (result) => {
      queryClient.setQueryData(queryKey, result)
      onSaved?.(value)
      save.succeed()
    },
    onError: (error) => save.fail(fallbackMessage(error, t('fallbackError'))),
  })

  return {
    value,
    change: (next: TValue) => {
      setValue(next)
      save.markDirty()
    },
    save,
    submit: () => {
      save.begin()
      mutation.mutate()
    },
  }
}
