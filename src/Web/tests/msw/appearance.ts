import { http, HttpResponse } from 'msw'
import type { AppearanceSettings } from '../../src/settings/appearanceApi'

/**
 * Registered globally in `tests/setup.ts`, not per test file — `_authenticated.tsx` fires
 * this request on every protected-route mount, the same reason `alertsHandlers` exists.
 * A test that cares about a particular language calls `server.use(appearanceHandler(...))`
 * with its own value; that handler wins because MSW resolves the most recently added match.
 */
export const appearanceHandler = (settings: AppearanceSettings = { theme: 'System', language: 'en' }) =>
  http.get('*/api/settings/appearance', () => HttpResponse.json(settings))

export const defaultAppearanceHandler = appearanceHandler()
