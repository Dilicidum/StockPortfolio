import { useEffect, useState } from 'react'

/**
 * Returns `value` once it has stopped changing for `delayMs`.
 *
 * The cleanup is what makes it a debounce rather than a delay: every new value clears
 * the pending timer before setting its own, so a burst of keystrokes produces exactly
 * one settled value. It is also what makes React 19 StrictMode harmless — the doubled
 * effect clears its own first timer instead of leaving two running.
 */
export function useDebouncedValue<T>(value: T, delayMs: number): T {
  const [debounced, setDebounced] = useState(value)

  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delayMs)

    return () => clearTimeout(timer)
  }, [value, delayMs])

  return debounced
}
