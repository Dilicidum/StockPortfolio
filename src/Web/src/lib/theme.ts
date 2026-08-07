export type ThemeChoice = 'light' | 'dark' | 'system'

const THEME_STORAGE_KEY = 'stockportfolio.theme'
const DARK_MEDIA_QUERY = '(prefers-color-scheme: dark)'

export function isThemeChoice(value: string | null | undefined): value is ThemeChoice {
  return value === 'light' || value === 'dark' || value === 'system'
}

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
  }
}

function systemPrefersDark(): boolean {
  return globalThis.matchMedia?.(DARK_MEDIA_QUERY).matches ?? false
}

export function applyTheme(choice: ThemeChoice): void {
  const dark = choice === 'dark' || (choice === 'system' && systemPrefersDark())
  document.documentElement.setAttribute('data-theme', dark ? 'dark' : 'light')
  document.documentElement.style.colorScheme = dark ? 'dark' : 'light'
}

export function watchSystemTheme(onChange: () => void): () => void {
  const query = globalThis.matchMedia(DARK_MEDIA_QUERY)
  query.addEventListener('change', onChange)
  return () => query.removeEventListener('change', onChange)
}
