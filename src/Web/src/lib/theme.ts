// Storage is a bootstrap cache only, read synchronously by the inline script in index.html
// before React exists. Once signed in, the server (GET/PUT /api/settings/appearance) is the
// source of truth; a later task wires that call and writes the result back through cacheTheme.

export type ThemeChoice = 'light' | 'dark' | 'system'

const THEME_STORAGE_KEY = 'stockportfolio.theme'
const DARK_MEDIA_QUERY = '(prefers-color-scheme: dark)'

/** Exported so a caller reading an untrusted string — the appearance API's response, notably — can guard it the same way `readCachedTheme` does. */
export function isThemeChoice(value: string | null | undefined): value is ThemeChoice {
  return value === 'light' || value === 'dark' || value === 'system'
}

/** Never throws — a browser with site data blocked reports 'system' rather than failing. */
export function readCachedTheme(): ThemeChoice {
  try {
    const stored = globalThis.localStorage?.getItem(THEME_STORAGE_KEY) ?? null
    return isThemeChoice(stored) ? stored : 'system'
  } catch {
    return 'system'
  }
}

export function cacheTheme(choice: ThemeChoice): void {
  try {
    globalThis.localStorage?.setItem(THEME_STORAGE_KEY, choice)
  } catch {
    // Private-mode Safari and friends. The theme still applies for this load.
  }
}

function systemPrefersDark(): boolean {
  return globalThis.matchMedia?.(DARK_MEDIA_QUERY).matches ?? false
}

/** Sets data-theme and colorScheme together — the second keeps native controls in sync in all three modes. */
export function applyTheme(choice: ThemeChoice): void {
  const dark = choice === 'dark' || (choice === 'system' && systemPrefersDark())
  document.documentElement.setAttribute('data-theme', dark ? 'dark' : 'light')
  document.documentElement.style.colorScheme = dark ? 'dark' : 'light'
}

/** Returns its own teardown — required under React 19 StrictMode, which mounts effects twice. */
export function watchSystemTheme(onChange: () => void): () => void {
  const query = globalThis.matchMedia(DARK_MEDIA_QUERY)
  query.addEventListener('change', onChange)
  return () => query.removeEventListener('change', onChange)
}
