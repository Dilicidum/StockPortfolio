import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'

import commonEn from '../locales/en/common.json'
import authEn from '../locales/en/auth.json'
import portfolioEn from '../locales/en/portfolio.json'
import dashboardEn from '../locales/en/dashboard.json'
import alertsEn from '../locales/en/alerts.json'
import settingsEn from '../locales/en/settings.json'
import errorsEn from '../locales/en/errors.json'

import commonUk from '../locales/uk/common.json'
import authUk from '../locales/uk/auth.json'
import portfolioUk from '../locales/uk/portfolio.json'
import dashboardUk from '../locales/uk/dashboard.json'
import alertsUk from '../locales/uk/alerts.json'
import settingsUk from '../locales/uk/settings.json'
import errorsUk from '../locales/uk/errors.json'

export const SUPPORTED_LANGUAGES = ['en', 'uk'] as const
export type Language = (typeof SUPPORTED_LANGUAGES)[number]

export const NAMESPACES = [
  'common',
  'auth',
  'portfolio',
  'dashboard',
  'alerts',
  'settings',
  'errors',
] as const

export function isSupportedLanguage(value: string | null | undefined): value is Language {
  return (SUPPORTED_LANGUAGES as readonly string[]).includes(value ?? '')
}

/**
 * A pre-sign-in bootstrap cache only, exactly like `lib/theme.ts`'s `THEME_STORAGE_KEY` — read
 * once to guess a starting language before any session exists. The moment one does,
 * `applyServerLanguage` is the only thing allowed to change the language, and it re-caches
 * here so the NEXT bootstrap (a fresh tab, before that session's appearance query has
 * answered) guesses the same value rather than falling back to English again.
 */
const LANGUAGE_STORAGE_KEY = 'stockportfolio.language'

/** Never throws — a browser with site data blocked reports 'en' rather than failing. */
export function readCachedLanguage(): Language {
  try {
    const stored = globalThis.localStorage?.getItem(LANGUAGE_STORAGE_KEY) ?? null
    return isSupportedLanguage(stored) ? stored : 'en'
  } catch {
    return 'en'
  }
}

function cacheLanguage(language: Language): void {
  try {
    globalThis.localStorage?.setItem(LANGUAGE_STORAGE_KEY, language)
  } catch {
    // Private-mode Safari and friends. The language still applies for this load.
  }
}

void i18n.use(initReactI18next).init({
  resources: {
    en: {
      common: commonEn,
      auth: authEn,
      portfolio: portfolioEn,
      dashboard: dashboardEn,
      alerts: alertsEn,
      settings: settingsEn,
      errors: errorsEn,
    },
    uk: {
      common: commonUk,
      auth: authUk,
      portfolio: portfolioUk,
      dashboard: dashboardUk,
      alerts: alertsUk,
      settings: settingsUk,
      errors: errorsUk,
    },
  },
  lng: readCachedLanguage(),
  // NO FALLBACK. A missing Ukrainian key must render as its raw key path — ugly and visible
  // to whoever is looking at the Ukrainian UI — rather than silently showing English, which
  // would hide the gap from whoever added the key and show it to every Ukrainian reader.
  fallbackLng: false,
  ns: NAMESPACES,
  defaultNS: 'common',
  interpolation: { escapeValue: false },
})

/**
 * THE ONLY PATH allowed to change the language once a session exists. Called by
 * `useSyncServerLanguage` as soon as the appearance query resolves, so the value the user
 * actually chose always overrides whatever the pre-sign-in cache guessed — including when
 * the two disagree, which is the ordinary case for a returning user on a fresh browser.
 */
export function applyServerLanguage(language: Language): void {
  cacheLanguage(language)
  if (i18n.language !== language) void i18n.changeLanguage(language)
}

export default i18n
