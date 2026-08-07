import { beforeEach, describe, expect, it } from 'vitest'
import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse, type RequestHandler } from 'msw'
import {
  createMemoryHistory,
  createRouter,
  RouterProvider,
  type AnyRouter,
} from '@tanstack/react-router'
import { routeTree } from '../src/routeTree.gen'
import { authStore } from '../src/auth/authStore'
import { queryClient } from '../src/lib/queryClient'
import { __resetRefreshInFlight } from '../src/lib/apiClient'
import { holdingKeys, type Holding } from '../src/portfolio/holdingsApi'
import type { AlertSetting, FiredAlert } from '../src/alerts/alertsApi'
import { ALERT_METHOD_NAME } from '../src/alerts/alertsApi'
import { FakeHubConnection } from './fakeHubConnection'
import { alertHistoryHandler, alertSettingsHandler, firedAlert, notificationOf } from './msw/alerts'
import { dashboardHandler, marketDataHealthHandler } from './msw/dashboard'
import { emptyTickerSearchHandler } from './msw/tickerSearch'
import { server } from './msw/server'

const AAPL: Holding = {
  id: '0199a1f0-0000-7000-8000-000000000001',
  ticker: 'AAPL',
  name: 'Apple Inc',
  quantity: 10,
  averagePrice: { amount: '100', currency: 'USD' },
  invested: { amount: '1000', currency: 'USD' },
  isVisible: true,
  updatedAt: '2026-08-04T12:00:00+00:00',
}

beforeEach(() => {
  authStore.signOut()
  queryClient.clear()
  __resetRefreshInFlight()
})

async function renderAt(path: string, heading: string, handlers: RequestHandler[] = []) {
  authStore.setUser({ id: 'u-1', email: 'holder@example.com' })
  queryClient.setQueryData(holdingKeys.list(), [AAPL])

  server.use(
    ...handlers,
    alertHistoryHandler(),
    alertSettingsHandler(),
    dashboardHandler,
    marketDataHealthHandler,
    emptyTickerSearchHandler,
  )

  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: [path] }),
    context: { queryClient, auth: authStore },
    defaultPreload: false,
  })

  render(<RouterProvider router={router as AnyRouter} />)
  await screen.findByRole('heading', { name: heading })

  return router
}

async function stream(): Promise<FakeHubConnection> {
  await waitFor(() => expect(FakeHubConnection.latest()).not.toBeNull())
  return FakeHubConnection.latest()!
}

const panelRows = () => within(screen.getByRole('list', { name: 'Recent alerts' })).getAllByRole('listitem')

const row = () => within(screen.getByRole('table'))

const longHistory = (): FiredAlert[] =>
  Array.from({ length: 8 }, (_, index) => firedAlert({ ticker: `T${index}` }))

describe('alerts in the browser', () => {
  it('renders a burst of pushed alerts newest first', async () => {
    await renderAt('/dashboard', 'Dashboard')

    await screen.findByText(/nothing has crossed a threshold yet/i)

    const connection = await stream()

    expect(screen.queryByRole('list', { name: 'Recent alerts' })).not.toBeInTheDocument()
    expect(screen.getByText(/nothing has crossed a threshold yet/i)).toBeInTheDocument()

    const first = firedAlert({ ticker: 'MSFT' })
    const second = firedAlert({ ticker: 'TSLA', direction: 'Rise', changePercent: '6.43' })

    act(() => {
      connection.push(ALERT_METHOD_NAME, notificationOf(first))
      connection.push(ALERT_METHOD_NAME, notificationOf(second))
    })

    await waitFor(() => expect(panelRows()).toHaveLength(2))

    expect(panelRows()[0]).toHaveTextContent('TSLA')
    expect(panelRows()[1]).toHaveTextContent('MSFT')

    expect(panelRows()[0]).toHaveTextContent(/142\.00/)
  })

  it('shows exactly "Live (WebSocket)" once the connection is up, and never claims SSE', async () => {
    await renderAt('/dashboard', 'Dashboard')

    await stream()

    expect(await screen.findByText('Live (WebSocket)')).toBeInTheDocument()
    expect(screen.queryByText(/\bSSE\b/)).not.toBeInTheDocument()
    expect(screen.queryByText(/server-sent/i)).not.toBeInTheDocument()
  })

  it('shows only the newest few on the dashboard panel, and says how many were left out', async () => {
    await renderAt('/dashboard', 'Dashboard', [alertHistoryHandler(longHistory())])

    await waitFor(() => expect(panelRows()).toHaveLength(6))
    expect(screen.getByRole('link', { name: /see all 8/i })).toBeInTheDocument()
  })

  it('lists the whole history on the notifications screen, off the same query', async () => {
    await renderAt('/notifications', 'Notifications', [alertHistoryHandler(longHistory())])

    await waitFor(() => expect(panelRows()).toHaveLength(8))
    expect(screen.queryByRole('link', { name: /see all/i })).not.toBeInTheDocument()
  })

  it('sends Simulate through the API and badges the alert it produces', async () => {
    let simulated = 0
    let historyReads = 0

    const simulatedAlert = firedAlert({ ticker: 'AAPL', isSimulated: true })

    await renderAt('/dashboard', 'Dashboard', [
      http.post('*/api/alerts/simulate', () => {
        simulated += 1
        return new HttpResponse(null, { status: 202 })
      }),
      http.get('*/api/alerts', () => {
        historyReads += 1
        return HttpResponse.json(historyReads === 1 ? [] : [simulatedAlert])
      }),
    ])

    await screen.findByText(/nothing has crossed a threshold yet/i)

    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: /simulate/i }))

    await waitFor(() => expect(simulated).toBe(1))
    await waitFor(() => expect(panelRows()).toHaveLength(1))

    expect(panelRows()[0]).toHaveTextContent(/simulated/i)
  })

  it('reports a 409 from Simulate as something the user can act on', async () => {
    await renderAt('/dashboard', 'Dashboard', [
      http.post('*/api/alerts/simulate', () =>
        HttpResponse.json(
          { title: 'Conflict', detail: 'No position to simulate.', status: 409 },
          { status: 409, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    ])

    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: /simulate/i }))

    expect(await screen.findByText(/set a threshold on one of your positions first/i)).toBeInTheDocument()
  })

  it('sets a threshold from the position it belongs to', async () => {
    let saved: unknown = null

    const stored: AlertSetting = {
      ticker: 'AAPL',
      thresholdPercent: 7,
      windowMinutes: 30,
      enabled: true,
    }

    await renderAt('/portfolio', 'Portfolio', [
      http.put('*/api/alerts/settings', async ({ request }) => {
        saved = await request.json()
        return HttpResponse.json(stored)
      }),
    ])

    const user = userEvent.setup()
    await user.click(row().getByRole('button', { name: /set an alert on AAPL/i }))

    const threshold = screen.getByLabelText(/move of at least/i)
    await user.clear(threshold)
    await user.type(threshold, '7')

    await user.selectOptions(screen.getByLabelText('Within'), '30')
    expect(screen.getByRole('switch', { name: /alerting on/i })).toBeChecked()

    await user.click(screen.getByRole('button', { name: /save alert/i }))

    await waitFor(() =>
      expect(saved).toEqual({
        ticker: 'AAPL',
        thresholdPercent: 7,
        windowMinutes: 30,
        enabled: true,
      }),
    )
  })

  it('shows a stored threshold on the row it belongs to', async () => {
    const stored: AlertSetting = {
      ticker: 'AAPL',
      thresholdPercent: 5,
      windowMinutes: 15,
      enabled: true,
    }

    await renderAt('/portfolio', 'Portfolio', [alertSettingsHandler([stored])])

    await waitFor(() =>
      expect(row().getByRole('button', { name: /set an alert on AAPL/i })).toHaveTextContent(
        '5% / 15m',
      ),
    )
  })

  it('leaves the portfolio page working when the alerts module is down', async () => {
    await renderAt('/portfolio', 'Portfolio', [
      http.get('*/api/alerts/settings', () =>
        HttpResponse.json({ title: 'Not found', status: 404 }, { status: 404 }),
      ),
    ])

    expect(row().getByText('AAPL')).toBeInTheDocument()

    const user = userEvent.setup()
    await user.click(row().getByRole('button', { name: /set an alert on AAPL/i }))

    expect(screen.getByRole('heading', { name: /alert on AAPL/i })).toBeInTheDocument()
  })
})
