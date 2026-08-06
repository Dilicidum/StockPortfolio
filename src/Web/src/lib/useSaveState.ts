import { useEffect, useState } from 'react'

export type SaveState = 'idle' | 'saving' | 'saved' | 'error'

export interface SaveStateApi {
  state: SaveState
  error: string
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

export function useSyncWhileIdle<T>(value: T | undefined, state: SaveState, setValue: (value: T) => void): void {
  useEffect(() => {
    if (value !== undefined && state === 'idle') setValue(value)
  }, [value, state, setValue])
}
