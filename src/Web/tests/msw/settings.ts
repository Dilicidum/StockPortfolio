import { http, HttpResponse } from 'msw'
import type { AppearanceSettings } from '../../src/settings/appearanceApi'
import type { ApiKeyStatus, DashboardSettings } from '../../src/settings/settingsApi'

/**
 * One handler per route, following `tests/msw/alerts.ts`'s convention. Mounting the
 * settings screen fires five GETs at once (D6: no aggregate endpoint), so
 * `defaultSettingsHandlers` below is what a test reaches for when it does not care about
 * any one section's data — the same role `alertsHandlers` plays for the layout's requests.
 */

export const dashboardSettingsHandler = (settings: DashboardSettings = { refreshIntervalSeconds: 60 }) =>
  http.get('*/api/settings/dashboard', () => HttpResponse.json(settings))

/** Echoes whatever was submitted, so a save round-trips exactly what the form sent. */
export const saveDashboardSettingsHandler = http.put('*/api/settings/dashboard', async ({ request }) => {
  const body = (await request.json()) as DashboardSettings
  return HttpResponse.json(body)
})

export const saveAppearanceHandler = http.put('*/api/settings/appearance', async ({ request }) => {
  const body = (await request.json()) as AppearanceSettings
  return HttpResponse.json(body)
})

export const apiKeyStatusHandler = (
  status: ApiKeyStatus = { configured: false, lastFour: null, rejected: false },
) => http.get('*/api/settings/api-key', () => HttpResponse.json(status))

export const saveApiKeyAcceptedHandler = (lastFour = 'a1b2') =>
  http.post('*/api/settings/api-key', () =>
    HttpResponse.json({ configured: true, lastFour, rejected: false } satisfies ApiKeyStatus),
  )

/** A 400: the provider looked at the key and said no. Distinct from the 503 below. */
export const saveApiKeyRejectedHandler = http.post('*/api/settings/api-key', () =>
  HttpResponse.json(
    { title: 'Bad request', detail: 'The provider rejected this key.', status: 400 },
    { status: 400, headers: { 'Content-Type': 'application/problem+json' } },
  ),
)

/** A 503: the provider never answered at all, which is a different sentence in the UI. */
export const saveApiKeyUnavailableHandler = http.post('*/api/settings/api-key', () =>
  HttpResponse.json(
    { title: 'Service unavailable', detail: 'The provider could not answer.', status: 503 },
    { status: 503, headers: { 'Content-Type': 'application/problem+json' } },
  ),
)

export const removeApiKeyHandler = http.delete('*/api/settings/api-key', () => new HttpResponse(null, { status: 204 }))

export const setHoldingVisibilityHandler = (onSave?: (id: string, isVisible: boolean) => void) =>
  http.patch('*/api/holdings/:id/visibility', async ({ request, params }) => {
    const body = (await request.json()) as { isVisible: boolean }
    onSave?.(String(params.id), body.isVisible)
    return new HttpResponse(null, { status: 204 })
  })

/** The quiet default: nothing configured, the default interval, and every write echoes back. */
export const defaultSettingsHandlers = [
  dashboardSettingsHandler(),
  saveDashboardSettingsHandler,
  saveAppearanceHandler,
  apiKeyStatusHandler(),
  setHoldingVisibilityHandler(),
]
