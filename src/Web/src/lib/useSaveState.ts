import { useEffect, useState } from 'react'

/**
 * The save-state machine `AppearanceSection`, `LanguageSection`, `QuotesSection` and
 * `ApiKeySection` each declared byte-for-byte before this: idle while nothing is happening,
 * saving while the mutation is in flight, saved on success, error with a message on failure.
 */
export type SaveState = 'idle' | 'saving' | 'saved' | 'error'

export interface SaveStateApi {
  state: SaveState
  error: string
  /** Call from the field's onChange — a stale "Saved" or error must not linger over a value the user has since changed. */
  markDirty: () => void
  begin: () => void
  succeed: () => void
  fail: (message: string) => void
}

export function useSaveState(): SaveStateApi {
  const [state, setState] = useState<SaveState>('idle')
  const [error, setError] = useState('')

  return {
    state,
    error,
    markDirty: () => setState('idle'),
    begin: () => setState('saving'),
    succeed: () => setState('saved'),
    fail: (message: string) => {
      setState('error')
      setError(message)
    },
  }
}

/**
 * The sync-while-idle effect every section repeated: the server's value wins over a local
 * edit, but only while nothing is mid-edit — a background refetch overwriting an unsaved
 * choice would look like the page ignoring a click.
 */
export function useSyncWhileIdle<T>(value: T | undefined, state: SaveState, setValue: (value: T) => void): void {
  useEffect(() => {
    if (value !== undefined && state === 'idle') setValue(value)
  }, [value, state, setValue])
}

/** The fallback every section's `onError` used for anything that is not an `ApiError` with its own message. */
export function fallbackMessage(error: unknown, fallback: string): string {
  return error instanceof Error && error.message ? error.message : fallback
}
