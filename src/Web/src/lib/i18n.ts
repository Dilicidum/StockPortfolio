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

const LANGUAGE_STORAGE_KEY = 'stockportfolio.language'

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
  fallbackLng: false,
  ns: NAMESPACES,
  defaultNS: 'common',
  interpolation: { escapeValue: false },
})

i18n.on('languageChanged', (language) => {
  if (typeof document !== 'undefined') document.documentElement.lang = language
})
if (typeof document !== 'undefined') document.documentElement.lang = i18n.language

export function applyServerLanguage(language: Language): Promise<void> {
  cacheLanguage(language)
  if (i18n.language === language) return Promise.resolve()

  return i18n.changeLanguage(language).then(() => undefined)
}

export default i18n
