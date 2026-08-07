import { http, HttpResponse } from 'msw'
import type { AppearanceSettings } from '../../src/settings/appearanceApi'

export const appearanceHandler = (settings: AppearanceSettings = { theme: 'system', language: 'en' }) =>
  http.get('*/api/settings/appearance', () => HttpResponse.json(settings))

export const defaultAppearanceHandler = appearanceHandler()
