import type { TFunction } from 'i18next'
import type { FieldValues, Path, UseFormSetError } from 'react-hook-form'
import { ApiError } from './apiClient'
import i18n from './i18n'

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
 * The MESSAGE TEXT here is whatever the server sent — English, regardless of the UI
 * language. Localising it would mean the API returning a language-negotiated body, which
 * no phase has scheduled; see the i18n task report for this recorded as left untranslated.
 * `i18n.t` below is only for the two client-side fallback strings that never came from a
 * response at all.
 *
 * Returns the banner text, or null when every problem was placed on a field.
 */
export function applyServerErrors<T extends FieldValues>(
  error: unknown,
  setError: UseFormSetError<T>,
  knownFields: readonly Path<T>[],
): string {
  if (!(error instanceof ApiError)) {
    return error instanceof Error && error.message ? error.message : i18n.t('common:fallbackError')
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

/**
 * The convention `portfolio.tsx`'s `addHoldingSchema` comment names: every validation
 * message is a literal key like `"errors.ticker.format"` rather than a sentence, so it can
 * be translated without editing a validation rule. The leading `errors.` names the
 * NAMESPACE, spelled with a dot to match every existing message rather than i18next's own
 * `:` separator, so this strips it and looks the remainder up in that namespace explicitly.
 *
 * A field can also carry a message `applyServerErrors` set from the API's own response text,
 * which is ordinary English prose, not a key — passed through unchanged rather than handed
 * to `t()`, which (with a plain string containing `.` and no matching resource) would try to
 * walk it as a nested key path instead of rendering the sentence.
 *
 * Returns `undefined` for no message, exactly as the field's `error` prop expects.
 */
export function translateFieldError(t: TFunction, message: string | undefined): string | undefined {
  if (!message) return undefined
  if (!message.startsWith('errors.')) return message

  return t(message.slice('errors.'.length), { ns: 'errors' })
}
