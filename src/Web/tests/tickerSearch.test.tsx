import { beforeEach, describe, expect, it } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
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
import type { GetDashboardResult } from '../src/marketdata/dashboardApi'
import type { TickerSuggestion } from '../src/marketdata/tickerSearchApi'
import { alertsHandlers } from './msw/alerts'
import { marketDataHealthHandler } from './msw/dashboard'
import { emptyTickerSearchHandler, tickerSearchHandler } from './msw/tickerSearch'
import { server } from './msw/server'

/**
 * Three symbols that all match "app", which is what makes the keyboard test mean
 * something — arrowing to the SECOND row can only be told apart from arrowing to the
 * first if there is more than one.
 */
const CATALOGUE: TickerSuggestion[] = [
  { symbol: 'AAPL', description: 'Apple Inc' },
  { symbol: 'APP', description: 'AppLovin Corp' },
  { symbol: 'APLE', description: 'Apple Hospitality REIT' },
]

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

/** The row every deployed account already has: added before names existed, so it has none. */
const NAMELESS: Holding = {
  ...AAPL,
  id: '0199a1f0-0000-7000-8000-000000000002',
  ticker: 'TSLA',
  name: null,
}

// The QueryClient is a module singleton shared by every test FILE in the run, so a seeded
// list — or a cached search result — leaks into the next file unless it is cleared here.
beforeEach(() => {
  authStore.signOut()
  queryClient.clear()
  __resetRefreshInFlight()
})

/** The fifth inline copy of the memory-router boilerplate, which is the convention here. */
async function renderPortfolio(seed: Holding[] = [AAPL]) {
  authStore.setUser({ email: 'holder@example.com' })
  queryClient.setQueryData(holdingKeys.list(), seed)

  // The authenticated layout opens the alert stream and the rows read their thresholds.
  server.use(...alertsHandlers)

  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ['/portfolio'] }),
    context: { queryClient, auth: authStore },
    defaultPreload: false,
  })

  render(<RouterProvider router={router as AnyRouter} />)
  await screen.findByRole('heading', { name: 'Portfolio' })

  return router
}

async function renderDashboard(data: GetDashboardResult) {
  authStore.setUser({ email: 'holder@example.com' })
  server.use(
    marketDataHealthHandler,
    ...alertsHandlers,
    http.get('*/api/dashboard', () => HttpResponse.json(data)),
  )

  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ['/dashboard'] }),
    context: { queryClient, auth: authStore },
    defaultPreload: false,
  })

  render(<RouterProvider router={router as AnyRouter} />)
  await screen.findByRole('heading', { name: 'Dashboard' })

  return router
}

/**
 * `Table` renders the desktop table AND the mobile card list into the DOM at every width —
 * CSS `display:none` picks one and jsdom applies no CSS. An unscoped `getByText('AAPL')`
 * therefore finds two nodes and throws, so every row query goes through here.
 */
const row = () => within(screen.getByRole('table'))

const tickerBox = () => screen.getByRole('combobox', { name: /ticker/i })

describe('ticker search', () => {
  /*
   * THE DEBOUNCE, asserted as a request COUNT rather than as a visible list. Without the
   * debounce this passes every assertion about what is on screen — the list for "appl" is
   * the same list either way — and sends four requests to get there. Only the counter and
   * the recorded query strings can tell the two apart.
   */
  it('sends one search for a burst of keystrokes, for the whole word', async () => {
    const queries: string[] = []

    server.use(
      http.get('*/api/marketdata/search', ({ request }) => {
        queries.push(new URL(request.url).searchParams.get('q') ?? '')
        return HttpResponse.json(CATALOGUE)
      }),
    )

    // `delay: null` dispatches the four keystrokes with no wait between them, which is
    // what a real burst of typing looks like against a 250ms window.
    const user = userEvent.setup({ delay: null })
    await renderPortfolio()

    await user.type(tickerBox(), 'appl')

    await waitFor(() => expect(screen.getByRole('listbox')).toBeInTheDocument())

    expect(queries).toEqual(['appl'])
  })

  it('does not search a query too short to match anything', async () => {
    let searches = 0

    server.use(
      http.get('*/api/marketdata/search', () => {
        searches += 1
        return HttpResponse.json(CATALOGUE)
      }),
    )

    const user = userEvent.setup({ delay: null })
    await renderPortfolio()

    await user.type(tickerBox(), 'a')

    // Long enough for the 250ms window to have closed twice over. The server answers `[]`
    // to a one-character query without calling the provider, so the round trip could only
    // ever return nothing.
    await new Promise((resolve) => setTimeout(resolve, 600))

    expect(searches).toBe(0)
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
  })

  it('fills the field when a suggestion is picked', async () => {
    server.use(tickerSearchHandler(CATALOGUE))

    const user = userEvent.setup()
    await renderPortfolio()

    await user.type(tickerBox(), 'appl')

    const listbox = await screen.findByRole('listbox')

    // The company name is beside the symbol, which is half the point of searching.
    expect(within(listbox).getByText('Apple Inc')).toBeInTheDocument()

    await user.click(within(listbox).getByRole('option', { name: /AAPL/ }))

    // The SYMBOL, not the text that was typed and not the description.
    expect(tickerBox()).toHaveValue('AAPL')
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
    expect(tickerBox()).toHaveAttribute('aria-expanded', 'false')
  })

  /*
   * Arrow keys, Enter and Escape, with DOM focus never leaving the input — that is what
   * `aria-activedescendant` buys and why the pattern is worth hand-building. A test that
   * only clicked options would pass with no keyboard support at all.
   */
  it('moves through matches with the arrow keys and picks one with Enter', async () => {
    server.use(tickerSearchHandler(CATALOGUE))

    const user = userEvent.setup()
    await renderPortfolio()

    await user.type(tickerBox(), 'app')
    await screen.findByRole('listbox')

    // Nothing is highlighted until a key says so, so Enter here would submit the form.
    expect(tickerBox()).not.toHaveAttribute('aria-activedescendant')

    await user.keyboard('{ArrowDown}{ArrowDown}')

    const highlighted = screen.getByRole('option', { selected: true })
    expect(highlighted).toHaveTextContent('APP')
    expect(tickerBox()).toHaveAttribute('aria-activedescendant', highlighted.id)
    expect(document.activeElement).toBe(tickerBox())

    // Up from the second row is the first, not nothing.
    await user.keyboard('{ArrowUp}')
    expect(screen.getByRole('option', { selected: true })).toHaveTextContent('AAPL')

    await user.keyboard('{ArrowDown}{Enter}')

    expect(tickerBox()).toHaveValue('APP')
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
  })

  it('closes the list on Escape and keeps what was typed', async () => {
    server.use(tickerSearchHandler(CATALOGUE))

    const user = userEvent.setup()
    await renderPortfolio()

    await user.type(tickerBox(), 'app')
    await screen.findByRole('listbox')

    await user.keyboard('{Escape}')

    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
    expect(tickerBox()).toHaveValue('app')
  })

  /*
   * The rule the whole feature is built around: picking from the list is a convenience,
   * never a requirement. Matches are on screen and deliberately ignored.
   */
  it('submits what was typed when no suggestion is picked', async () => {
    let posted: unknown = null

    const msft: Holding = { ...AAPL, ticker: 'MSFT', name: 'Microsoft Corp' }

    server.use(
      tickerSearchHandler(CATALOGUE),
      http.post('*/api/holdings', async ({ request }) => {
        posted = await request.json()
        return HttpResponse.json(msft, { status: 201 })
      }),
      http.get('*/api/holdings', () => HttpResponse.json([msft])),
    )

    const user = userEvent.setup()
    await renderPortfolio()

    await user.type(tickerBox(), 'appl')
    await screen.findByRole('listbox')

    // Straight past the open list to the next field, then submit.
    await user.type(screen.getByLabelText(/quantity/i), '5')
    await user.type(screen.getByLabelText(/price/i), '300')
    await user.click(screen.getByRole('button', { name: /add position/i }))

    // "appl" — the letters typed, not AAPL, and not the first suggestion silently applied.
    await waitFor(() => expect(posted).toEqual({ ticker: 'appl', quantity: 5, price: 300 }))
  })

  /*
   * The outage case, and it is indistinguishable from "nothing matched" by design: the
   * endpoint answers `200 []` however badly the provider is doing. So this one test covers
   * both, and what it asserts is that the field is exactly the plain text box it was
   * before the feature existed.
   */
  it('leaves the form working when search returns nothing', async () => {
    let posted: unknown = null

    const msft: Holding = { ...AAPL, ticker: 'MSFT', name: null }

    server.use(
      emptyTickerSearchHandler,
      http.post('*/api/holdings', async ({ request }) => {
        posted = await request.json()
        return HttpResponse.json(msft, { status: 201 })
      }),
      http.get('*/api/holdings', () => HttpResponse.json([msft])),
    )

    const user = userEvent.setup()
    await renderPortfolio()

    await user.type(tickerBox(), 'MSFT')

    // No popup, and no "no matches" panel either — an empty answer must not look like a
    // problem the user has to dismiss before typing the rest of the form.
    await waitFor(() => expect(tickerBox()).toHaveAttribute('aria-expanded', 'false'))
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()

    await user.type(screen.getByLabelText(/quantity/i), '5')
    await user.type(screen.getByLabelText(/price/i), '300')
    await user.click(screen.getByRole('button', { name: /add position/i }))

    await waitFor(() => expect(posted).toEqual({ ticker: 'MSFT', quantity: 5, price: 300 }))
    await waitFor(() => expect(row().getByText('MSFT')).toBeInTheDocument())
  })

  it('shows the company name on the holdings table, and the ticker alone without one', async () => {
    server.use(emptyTickerSearchHandler)
    await renderPortfolio([AAPL, NAMELESS])

    expect(row().getByText('AAPL')).toBeInTheDocument()
    expect(row().getByText('Apple Inc')).toBeInTheDocument()

    // A missing name is not an error and not a pending value: no dash, no placeholder,
    // nothing but the ticker. The whole Asset cell reads "TSLA".
    const tsla = row().getByRole('row', { name: /TSLA/ })
    expect(within(tsla).getAllByRole('cell')[0]).toHaveTextContent(/^TSLA$/)
  })

  it('shows the company name on the dashboard table, and the ticker alone without one', async () => {
    const zero = { amount: '0', currency: 'USD' }

    const data: GetDashboardResult = {
      positions: [
        {
          id: AAPL.id,
          ticker: 'AAPL',
          name: 'Apple Inc',
          quantity: 10,
          averagePrice: { amount: '100', currency: 'USD' },
          cost: { amount: '1000', currency: 'USD' },
          currentPrice: { amount: '150', currency: 'USD' },
          marketValue: { amount: '1500', currency: 'USD' },
          profit: { amount: '500', currency: 'USD' },
          profitPercent: '50.00',
          weight: '100.00',
          observedAt: '2026-08-05T12:00:04+00:00',
          isLastKnown: false,
        },
        {
          id: NAMELESS.id,
          ticker: 'TSLA',
          name: null,
          quantity: 5,
          averagePrice: { amount: '200', currency: 'USD' },
          cost: { amount: '1000', currency: 'USD' },
          currentPrice: null,
          marketValue: null,
          profit: null,
          profitPercent: null,
          weight: null,
          observedAt: null,
          isLastKnown: false,
        },
      ],
      totals: {
        value: { amount: '1500', currency: 'USD' },
        cost: { amount: '2000', currency: 'USD' },
        profit: zero,
        profitPercent: '0.00',
        positionCount: 2,
        pricedPositionCount: 1,
      },
      asOf: '2026-08-05T12:00:05+00:00',
      stalestObservedAt: '2026-08-05T12:00:04+00:00',
    }

    await renderDashboard(data)

    await waitFor(() => expect(row().getByText('Apple Inc')).toBeInTheDocument())

    // The unpriced row still renders its dashes; the Asset cell is not one of them.
    const tsla = row().getByRole('row', { name: /TSLA/ })
    expect(within(tsla).getAllByRole('cell')[0]).toHaveTextContent(/^TSLA$/)
  })

  // Leaving the field stops the searching, so tabbing on through a half-typed symbol does
  // not leave a request in flight behind a form the user has already moved past.
  it('stops searching once the field is left', async () => {
    let searches = 0

    server.use(
      http.get('*/api/marketdata/search', () => {
        searches += 1
        return HttpResponse.json(CATALOGUE)
      }),
    )

    const user = userEvent.setup({ delay: null })
    await renderPortfolio()

    await user.type(tickerBox(), 'appl')
    await user.tab()

    await new Promise((resolve) => setTimeout(resolve, 600))

    expect(searches).toBe(0)
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
  })

  it('places a server-side ticker error under the combobox', async () => {
    server.use(
      emptyTickerSearchHandler,
      http.post('*/api/holdings', () =>
        HttpResponse.json(
          { title: 'Bad request', status: 400, errors: { Ticker: ['No such symbol.'] } },
          { status: 400, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
      // `useAddHolding`'s onSettled invalidates the list, and query-core awaits that
      // before it rejects `mutateAsync` — so without this handler the submit never
      // settles and the error is never placed.
      http.get('*/api/holdings', () => HttpResponse.json([AAPL])),
    )

    const user = userEvent.setup()
    await renderPortfolio()

    await user.type(tickerBox(), 'ZZZZ')
    await user.type(screen.getByLabelText(/quantity/i), '5')
    await user.type(screen.getByLabelText(/price/i), '300')
    await user.click(screen.getByRole('button', { name: /add position/i }))

    expect(await screen.findByText('No such symbol.')).toBeInTheDocument()
    expect(tickerBox()).toHaveAttribute('aria-invalid', 'true')
  })
})
