import { beforeEach, describe, expect, it } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { delay, http, HttpResponse } from 'msw'
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
import { alertsHandlers } from './msw/alerts'
import { emptyTickerSearchHandler } from './msw/tickerSearch'
import { server } from './msw/server'

const AAPL: Holding = {
  id: '0199a1f0-0000-7000-8000-000000000001',
  ticker: 'AAPL',
  // Null on purpose: a position recorded before ticker search existed, which is what the
  // deployed database is full of. `tickerSearch.test.tsx` covers the named case.
  name: null,
  quantity: 10,
  averagePrice: { amount: '100', currency: 'USD' },
  invested: { amount: '1000', currency: 'USD' },
  isVisible: true,
  updatedAt: '2026-08-04T12:00:00+00:00',
}

// The QueryClient is a module singleton shared by every test FILE in the run, so a
// seeded holdings list leaks into the next file unless it is cleared here.
beforeEach(() => {
  authStore.signOut()
  queryClient.clear()
  __resetRefreshInFlight()
})

/**
 * The third inline copy of the memory-router boilerplate, which is the convention here —
 * `auth.test.tsx` and `sessionPersistence.test.tsx` are the first two.
 *
 * It mounts the real route rather than `<PortfolioPage />` on its own, because the page
 * renders `AppShell`, `AppShell` calls `useAuth()`, and `useAuth` throws outside
 * `<AuthProvider>` — which in turn calls `useRouter()` and so needs a router anyway.
 *
 * Seeding the cache first means the route's `loader` resolves out of it and no GET is
 * issued on mount, so every GET a test observes is a deliberate refetch.
 */
async function renderPortfolio(seed: Holding[] = [AAPL]) {
  authStore.setUser({ id: 'u-1', email: 'holder@example.com' })
  queryClient.setQueryData(holdingKeys.list(), seed)

  // The ticker field searches as you type; every test below types into it. It is added
  // FIRST so a test that wants real matches can still shadow it with its own handler.
  // The alert handlers come with it: the layout opens the stream and the rows read their
  // thresholds, so a portfolio mount is three alert requests before anything is typed.
  server.use(emptyTickerSearchHandler, ...alertsHandlers)

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

/**
 * `Table` renders the desktop table AND the mobile card list into the DOM at every width —
 * CSS `display:none` picks one and jsdom applies no CSS. An unscoped `getByText('AAPL')`
 * therefore finds two nodes and throws, so every row query goes through here.
 */
const row = () => within(screen.getByRole('table'))

/**
 * The correction panel carries the same two field labels as "Add a position", so every
 * query into it is scoped. A <form> only exposes role="form" when it has an accessible
 * name, which is what the panel's aria-labelledby heading provides.
 */
const editPanel = (ticker = 'AAPL') =>
  within(screen.getByRole('form', { name: new RegExp(`correct ${ticker}`, 'i') }))

describe('portfolio', () => {
  it('adds a position and shows it in the table', async () => {
    const msft: Holding = {
      ...AAPL,
      id: '0199a1f0-0000-7000-8000-000000000002',
      ticker: 'MSFT',
      quantity: 5,
      averagePrice: { amount: '300', currency: 'USD' },
      invested: { amount: '1500', currency: 'USD' },
    }

    server.use(
      http.post('*/api/holdings', () => HttpResponse.json(msft, { status: 201 })),
      http.get('*/api/holdings', () => HttpResponse.json([AAPL, msft])),
    )

    const user = userEvent.setup()
    await renderPortfolio()

    await user.type(screen.getByLabelText(/ticker/i), 'MSFT')
    await user.type(screen.getByLabelText(/quantity/i), '5')
    await user.type(screen.getByLabelText(/price/i), '300')
    await user.click(screen.getByRole('button', { name: /add position/i }))

    await waitFor(() => expect(row().getByText('MSFT')).toBeInTheDocument())

    // A plain purchase is not a merge: the quantity that came back is the one submitted.
    expect(screen.queryByText(/merged into your/i)).not.toBeInTheDocument()
  })

  it('shows the merge notice when the API reports a merged purchase', async () => {
    // 20 shares came back for a submitted 10 — that sum is what `addHolding` reads as a merge.
    const merged: Holding = {
      ...AAPL,
      quantity: 20,
      averagePrice: { amount: '125', currency: 'USD' },
      invested: { amount: '2500', currency: 'USD' },
    }

    server.use(
      http.post('*/api/holdings', () => HttpResponse.json(merged, { status: 200 })),
      http.get('*/api/holdings', () => HttpResponse.json([merged])),
    )

    const user = userEvent.setup()
    await renderPortfolio()

    await user.type(screen.getByLabelText(/ticker/i), 'AAPL')
    await user.type(screen.getByLabelText(/quantity/i), '10')
    await user.type(screen.getByLabelText(/price/i), '150')
    await user.click(screen.getByRole('button', { name: /add position/i }))

    // Alert tone="success" renders role="status" (polite), NOT role="alert".
    const notice = await screen.findByRole('status')
    expect(notice).toHaveTextContent(/merged/i)
    expect(notice).toHaveTextContent(/125/)

    // And the two buys really did collapse into one row rather than two.
    await waitFor(() => expect(row().getByText('20')).toBeInTheDocument())
    expect(row().getAllByText('AAPL')).toHaveLength(1)
  })

  /*
   * The "U" of P0's CRUD, and the step phase-2-my-portfolio.md §8 asks for in a real
   * browser: "Edit that row to 15 @ $120 -> 15 shares @ $120, Invested = $1,800".
   * Before this test the edit path had no call site at all — `useUpdateHolding` and
   * `updateHolding` were written, tested on the server, and unreachable from the UI.
   */
  it('corrects a row and shows the new values', async () => {
    const corrected: Holding = {
      ...AAPL,
      quantity: 15,
      averagePrice: { amount: '120', currency: 'USD' },
      invested: { amount: '1800', currency: 'USD' },
    }

    let patched: unknown = null

    server.use(
      http.patch('*/api/holdings/:id', async ({ request }) => {
        patched = await request.json()
        return HttpResponse.json(corrected)
      }),
      http.get('*/api/holdings', () => HttpResponse.json([corrected])),
    )

    const user = userEvent.setup()
    await renderPortfolio()

    await user.click(row().getByRole('button', { name: /edit aapl/i }))

    // Prefilled from the row, not blank: correcting a quantity should not mean
    // retyping a price that was already right.
    expect(editPanel().getByLabelText(/quantity/i)).toHaveValue(10)
    expect(editPanel().getByLabelText(/price/i)).toHaveValue(100)

    await user.clear(editPanel().getByLabelText(/quantity/i))
    await user.type(editPanel().getByLabelText(/quantity/i), '15')
    await user.clear(editPanel().getByLabelText(/price/i))
    await user.type(editPanel().getByLabelText(/price/i), '120')
    await user.click(editPanel().getByRole('button', { name: /^save$/i }))

    await waitFor(() => expect(row().getByText('15')).toBeInTheDocument())

    // Matched on the digits, not on "$120.00": `formatMoney` goes through
    // `toLocaleString`, and this runner's default locale is en-GB, which renders USD
    // as "US$120.00". Nothing else in the row contains 120.
    expect(row().getByText(/120/)).toBeInTheDocument()

    // The PATCH really carried the corrected numbers — asserting the table alone would
    // pass on the optimistic update even if the request body were wrong.
    expect(patched).toEqual({ quantity: 15, price: 120 })

    // The panel closes on success, so the page is not left holding a stale form.
    await waitFor(() => expect(screen.queryByRole('form', { name: /correct/i })).not.toBeInTheDocument())
  })

  // The edit twin of the delete test below, and it fails the same two ways: if the
  // optimistic update never happens, and if the failure is never reported.
  it('restores the row and reports the failure when a correction fails', async () => {
    let listCalls = 0

    server.use(
      http.patch('*/api/holdings/:id', async () => {
        await delay(150)
        return HttpResponse.json(
          { title: 'Server error', detail: 'Could not save the correction.', status: 500 },
          { status: 500 },
        )
      }),
      http.get('*/api/holdings', async () => {
        listCalls += 1
        await delay(1200)
        return HttpResponse.json([AAPL])
      }),
    )

    const user = userEvent.setup()
    await renderPortfolio()

    await user.click(row().getByRole('button', { name: /edit aapl/i }))
    await user.clear(editPanel().getByLabelText(/quantity/i))
    await user.type(editPanel().getByLabelText(/quantity/i), '15')
    await user.click(editPanel().getByRole('button', { name: /^save$/i }))

    // Optimistic: the table shows the correction before the server has answered.
    await waitFor(() => expect(row().getByText('15')).toBeInTheDocument())

    // Rolled back from the onMutate snapshot, inside a window the 1200ms refetch
    // cannot reach — so nothing but the rollback can have put 10 back.
    await waitFor(() => expect(row().getByText('10')).toBeInTheDocument(), { timeout: 500 })

    /*
     * And said so. A row that changes and changes back is indistinguishable from a
     * rendering glitch, which is the whole finding.
     *
     * The banner lands one refetch AFTER the rollback, not with it, and that is
     * query-core's doing rather than the page's: `Mutation.execute` awaits the
     * hook-level `onSettled` — which returns `invalidateQueries` — before it dispatches
     * the error, so neither `mutateAsync`'s rejection nor a per-call `onError` can fire
     * any earlier. This handler stalls the GET by 1200ms to prove the rollback is not
     * the refetch, so the banner is behind that stall here; in the browser it is one
     * round trip.
     */
    await waitFor(() => expect(queryClient.isFetching()).toBe(0), { timeout: 3000 })
    expect(await screen.findByRole('alert')).toHaveTextContent(/could not save the correction/i)

    expect(listCalls).toBe(1)
  })

  // THE ONE THAT EARNS ITS KEEP: it fails if the rollback reads the wrong callback parameter.
  it('restores the row when an optimistic delete fails', async () => {
    let listCalls = 0

    server.use(
      // Both handlers are deliberately slow, and the two delays do different jobs.
      // The DELETE's opens a window in which the optimistic removal is observable at
      // all — answer instantly and the rollback lands inside the same flush as the
      // click, so the "row is gone" assertion never sees it.
      http.delete('*/api/holdings/:id', async () => {
        await delay(150)
        return new HttpResponse(null, { status: 500 })
      }),
      // The GET's makes the middle assertion mean something. It still holds the row,
      // because the DELETE failed and the server never lost it — so with an instant
      // refetch this test would pass with no rollback code in the app at all.
      http.get('*/api/holdings', async () => {
        listCalls += 1
        await delay(1200)
        return HttpResponse.json([AAPL])
      }),
    )

    const user = userEvent.setup()
    await renderPortfolio()

    expect(row().getByText('AAPL')).toBeInTheDocument()

    await user.click(row().getByRole('button', { name: /remove aapl/i }))
    await user.click(screen.getByRole('button', { name: /^remove$/i }))

    // Optimistic: the row leaves before the server has answered. With no rows left,
    // `Table` renders its empty state and there is no <table> element at all.
    await waitFor(() => expect(screen.queryByRole('table')).not.toBeInTheDocument())

    // Rolled back from the onMutate snapshot. The bounded timeout is the assertion:
    // the refetch that onSettled fires cannot answer for another 1200ms, so nothing
    // but the rollback can put this row back inside this window.
    await waitFor(() => expect(row().getByText('AAPL')).toBeInTheDocument(), { timeout: 500 })

    // Let the refetch land, so the test does not finish with a request in flight.
    await waitFor(() => expect(queryClient.isFetching()).toBe(0), { timeout: 3000 })
    expect(listCalls).toBe(1)

    // The row coming back is not, on its own, a report of failure — it reads as a
    // glitch and invites a second click at a server that just 500'd. It is asserted
    // after the refetch for the reason spelled out in the correction test above:
    // query-core awaits `onSettled`'s `invalidateQueries` before rejecting.
    expect(await screen.findByRole('alert')).toHaveTextContent(/could not remove aapl/i)
  })

  it('rejects a 6-character ticker before submitting', async () => {
    let posts = 0

    server.use(
      http.post('*/api/holdings', () => {
        posts += 1
        return HttpResponse.json(AAPL, { status: 201 })
      }),
    )

    const user = userEvent.setup()
    await renderPortfolio()

    await user.type(screen.getByLabelText(/ticker/i), 'TOOLONG')
    await user.type(screen.getByLabelText(/quantity/i), '1')
    await user.type(screen.getByLabelText(/price/i), '1')
    await user.click(screen.getByRole('button', { name: /add position/i }))

    // Translated text, not the raw "errors.ticker.format" key — i18n now turns the
    // message-key convention into what a user actually sees. See tests/i18n.test.tsx for
    // the Ukrainian side of that same conversion.
    expect(await screen.findByText(/enter a valid ticker/i)).toBeInTheDocument()

    // A request COUNTER, not just a visible message: asserting the message alone would
    // pass even if the POST had ALSO been sent. Mirrors refreshDedupe.test.ts.
    expect(posts).toBe(0)
  })

  /*
   * The route's `errorComponent`. `useSuspenseQuery` defaults to `throwOnError: true`,
   * so without one this rejection replaces the whole page with TanStack Router's bare
   * built-in panel — no shell, no nav, no way back.
   *
   * 404 rather than 500 on purpose: `queryClient`'s retry predicate stops at 4xx but
   * retries 5xx twice with exponential backoff, which would make this a 3-second test
   * for no extra coverage.
   */
  it('keeps the shell and offers a retry when the holdings fetch fails', async () => {
    let calls = 0

    server.use(
      // This test builds its own router instead of calling `renderPortfolio`, so the
      // layout's alert requests need stubbing here too.
      ...alertsHandlers,
      http.get('*/api/holdings', () => {
        calls += 1
        return calls === 1
          ? HttpResponse.json(
              { title: 'Not found', detail: 'Holdings are unavailable.', status: 404 },
              { status: 404 },
            )
          : HttpResponse.json([AAPL])
      }),
    )

    const user = userEvent.setup()
    authStore.setUser({ id: 'u-1', email: 'holder@example.com' })

    // Deliberately NOT seeded, unlike renderPortfolio: the loader has to reach the
    // network for there to be a rejection to catch.
    const router = createRouter({
      routeTree,
      history: createMemoryHistory({ initialEntries: ['/portfolio'] }),
      context: { queryClient, auth: authStore },
      defaultPreload: false,
    })

    render(<RouterProvider router={router as AnyRouter} />)

    // The shell survived. The built-in panel renders neither of these.
    expect(await screen.findByRole('heading', { name: 'Portfolio' })).toBeInTheDocument()
    expect(screen.getByRole('navigation', { name: 'Main' })).toBeInTheDocument()
    expect(await screen.findByRole('alert')).toHaveTextContent(/holdings are unavailable/i)

    await user.click(screen.getByRole('button', { name: /try again/i }))

    await waitFor(() => expect(row().getByText('AAPL')).toBeInTheDocument())
    expect(calls).toBe(2)
  })
})
