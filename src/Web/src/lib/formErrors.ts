import type { FieldValues, Path, UseFormSetError } from 'react-hook-form'
import { ApiError } from './apiClient'

/**
 * Turns an RFC 7807 response into something a form can show.
 *
 * A 400 from the API carries `errors: { "Email": ["..."] }`. Those belong under
 * the field they name, not in a banner — a banner saying "one or more
 * validation errors occurred" is the least useful sentence in web development.
 * Anything that does not map to a known field (a 401, a 409, a 500, or a field
 * name the form does not have) becomes the banner message instead, so nothing
 * is ever silently dropped.
 *
 * Returns the banner text, or null when every problem was placed on a field.
 */
export function applyServerErrors<T extends FieldValues>(
  error: unknown,
  setError: UseFormSetError<T>,
  knownFields: readonly Path<T>[],
): string {
  if (!(error instanceof ApiError)) {
    return error instanceof Error && error.message
      ? error.message
      : 'Something went wrong. Please try again.'
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

  return error.message || 'Request failed.'
}
