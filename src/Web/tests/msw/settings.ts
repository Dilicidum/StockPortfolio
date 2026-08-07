import { http, HttpResponse } from 'msw'
import type { AppearanceSettings } from '../../src/settings/appearanceApi'
import type { ApiKeyStatus, DashboardSettings } from '../../src/settings/settingsApi'

export const dashboardSettingsHandler = (settings: DashboardSettings = { refreshIntervalSeconds: 60 }) =>
  http.get('*/api/settings/dashboard', () => HttpResponse.json(settings))

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

export const apiKeyStatusUnavailableHandler = http.get('*/api/settings/api-key', () =>
  HttpResponse.json(
    { title: 'Not found', status: 404 },
    { status: 404, headers: { 'Content-Type': 'application/problem+json' } },
  ),
)

export const saveApiKeyAcceptedHandler = (lastFour = 'a1b2') =>
  http.post('*/api/settings/api-key', () =>
    HttpResponse.json({ configured: true, lastFour, rejected: false } satisfies ApiKeyStatus),
  )

export const saveApiKeyRejectedHandler = http.post('*/api/settings/api-key', () =>
  HttpResponse.json(
    { title: 'Bad request', detail: 'The provider rejected this key.', status: 400 },
    { status: 400, headers: { 'Content-Type': 'application/problem+json' } },
  ),
)

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

export const setHoldingVisibilityFailingFor = (failingId: string) =>
  http.patch('*/api/holdings/:id/visibility', ({ params }) =>
    String(params.id) === failingId ? new HttpResponse(null, { status: 404 }) : new HttpResponse(null, { status: 204 }),
  )

export const defaultSettingsHandlers = [
  dashboardSettingsHandler(),
  saveDashboardSettingsHandler,
  saveAppearanceHandler,
  apiKeyStatusHandler(),
  setHoldingVisibilityHandler(),
]
