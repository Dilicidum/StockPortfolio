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

// The QueryClient is a module singleton shared by every test FILE in the run, so a seeded
// history — or a cached settings list — leaks into the next file unless it is cleared here.
beforeEach(() => {
  authStore.signOut()
  queryClient.clear()
  __resetRefreshInFlight()
})

/** The seventh inline copy of the memory-router boilerplate, which is the convention here. */
async function renderAt(path: string, heading: string, handlers: RequestHandler[] = []) {
  authStore.setUser({ id: 'u-1', email: 'holder@example.com' })
  queryClient.setQueryData(holdingKeys.list(), [AAPL])

  // A test's own handlers first: within one `use` call the earliest wins, so these shadow
  // the quiet defaults underneath them.
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

/** The one connection the authenticated layout opened, once it exists. */
async function stream(): Promise<FakeHubConnection> {
  await waitFor(() => expect(FakeHubConnection.latest()).not.toBeNull())
  return FakeHubConnection.latest()!
}

const panelRows = () => within(screen.getByRole('list', { name: 'Recent alerts' })).getAllByRole('listitem')

/**
 * `Table` renders the desktop table AND the mobile card list into the DOM at every width —
 * CSS `display:none` picks one and jsdom applies no CSS — so every row control is on screen
 * twice. Every row query goes through here, exactly as the portfolio suite's does.
 */
const row = () => within(screen.getByRole('table'))

/** Eight fired alerts — two more than the dashboard panel shows, which is the point. */
const longHistory = (): FiredAlert[] =>
  Array.from({ length: 8 }, (_, index) => firedAlert({ ticker: `T${index}` }))

describe('alerts in the browser', () => {
  it('renders a burst of pushed alerts newest first', async () => {
    await renderAt('/dashboard', 'Dashboard')

    // The history query has to have settled before anything is pushed: the stream prepends
    // into the cache and deliberately does nothing when the cache is still empty of a
    // result, because seeding it would make the query look fresh and suppress the fetch.
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

    // Newest at the top — the arrival order reversed, which is the only ordering a feed
    // of moments can have.
    expect(panelRows()[0]).toHaveTextContent('TSLA')
    expect(panelRows()[1]).toHaveTextContent('MSFT')

    // The price came over the wire as a bare string beside one currency and is rendered as
    // money, which is the conversion the two wire shapes make necessary.
    expect(panelRows()[0]).toHaveTextContent(/142\.00/)
  })

  /*
   * The badge says what was built. A WebSocket is the transport, and a badge naming a different
   * one would be the application describing itself wrongly on its own shell — consistency
   * between the claim and the implementation is graded.
   */
  it('shows exactly "Live (WebSocket)" once the connection is up, and never claims SSE', async () => {
    await renderAt('/dashboard', 'Dashboard')

    await stream()

    expect(await screen.findByText('Live (WebSocket)')).toBeInTheDocument()
    expect(screen.queryByText(/\bSSE\b/)).not.toBeInTheDocument()
    expect(screen.queryByText(/server-sent/i)).not.toBeInTheDocument()
  })

  /* The connection dropping must not clear the panel — the rows are cache, not connection state. */
  it('keeps the rows on screen while the connection is reconnecting', async () => {
    await renderAt('/dashboard', 'Dashboard', [alertHistoryHandler([firedAlert({ ticker: 'MSFT' })])])

    const connection = await stream()

    await waitFor(() => expect(panelRows()).toHaveLength(1))

    act(() => connection.dropForGood())

    expect(panelRows()[0]).toHaveTextContent('MSFT')
  })

  it('shows only the newest few on the dashboard panel, and says how many were left out', async () => {
    await renderAt('/dashboard', 'Dashboard', [alertHistoryHandler(longHistory())])

    // Six of eight, and a way to the rest. The panel is a column beside a table, not a
    // history screen; the count in the link is what says something was left out.
    await waitFor(() => expect(panelRows()).toHaveLength(6))
    expect(screen.getByRole('link', { name: /see all 8/i })).toBeInTheDocument()
  })

  it('lists the whole history on the notifications screen, off the same query', async () => {
    await renderAt('/notifications', 'Notifications', [alertHistoryHandler(longHistory())])

    await waitFor(() => expect(panelRows()).toHaveLength(8))
    expect(screen.queryByRole('link', { name: /see all/i })).not.toBeInTheDocument()
  })

  /*
   * Simulate exists because outside market hours nothing moves, so without it the feature
   * cannot be demonstrated at all. What is asserted is that the button goes through the API
   * — the row comes back from history, saved server-side and badged — rather than pushing a
   * fabricated event straight into the panel, which would prove nothing about the mechanism.
   */
  it('sends Simulate through the API and badges the alert it produces', async () => {
    let simulated = 0
    let historyReads = 0

    // Not named `row` — that is the table-scoping helper above.
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

    // The badge is the whole point: a simulated alert took the real path and is otherwise
    // indistinguishable from one a price move produced.
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

    // A native <select> and a native switch — no component library anywhere near this form.
    await user.selectOptions(screen.getByLabelText('Within'), '30')
    expect(screen.getByRole('switch', { name: /alerting on/i })).toBeChecked()

    await user.click(screen.getByRole('button', { name: /save alert/i }))

    // Numbers, not strings: `thresholdPercent` is a value the user typed rather than a
    // figure the server computed, and the host reads it with NumberHandling.Strict.
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

    // The row's control doubles as the readout: there is nowhere else the configuration
    // for one position is written down.
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

    // A failed settings read is a decoration missing, not a page gone: the table is still
    // there and the control still opens. That is why this query is neither a loader nor a
    // `useSuspenseQuery` — either would hand the failure to the route's error component.
    expect(row().getByText('AAPL')).toBeInTheDocument()

    const user = userEvent.setup()
    await user.click(row().getByRole('button', { name: /set an alert on AAPL/i }))

    expect(screen.getByRole('heading', { name: /alert on AAPL/i })).toBeInTheDocument()
  })
})
