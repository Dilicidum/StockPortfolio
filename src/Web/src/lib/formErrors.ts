import type { TFunction } from 'i18next'
import type { FieldValues, Path, UseFormSetError } from 'react-hook-form'
import { ApiError } from './apiClient'
import i18n from './i18n'

export function fallbackMessage(error: unknown, fallback: string): string {
  return error instanceof Error && error.message ? error.message : fallback
}

export function applyServerErrors<T extends FieldValues>(
  error: unknown,
  setError: UseFormSetError<T>,
  knownFields: readonly Path<T>[],
): string {
  if (!(error instanceof ApiError)) {
    return fallbackMessage(error, i18n.t('common:fallbackError'))
  }

  const fieldErrors = error.fieldErrors
  const unplaced: string[] = []
  let placedAny = false

  for (const [field, messages] of Object.entries(fieldErrors)) {
    const message = messages.join(' ')
    if ((knownFields as readonly string[]).includes(field)) {
      setError(field as Path<T>, { type: 'server', message })
      placedAny = true
    } else {
      unplaced.push(message)
    }
  }

  if (unplaced.length > 0) return unplaced.join(' ')
  if (placedAny) return ''

  return error.message || i18n.t('common:requestFailed')
}

export function translateFieldError(t: TFunction, message: string | undefined): string | undefined {
  if (!message) return undefined
  if (!message.startsWith('errors.')) return message

  return t(message.slice('errors.'.length), { ns: 'errors' })
}
