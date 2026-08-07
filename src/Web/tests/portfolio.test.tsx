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
  name: null,
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

async function renderPortfolio(seed: Holding[] = [AAPL]) {
  authStore.setUser({ id: 'u-1', email: 'holder@example.com' })
  queryClient.setQueryData(holdingKeys.list(), seed)

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

const row = () => within(screen.getByRole('table'))

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

    expect(screen.queryByText(/merged into your/i)).not.toBeInTheDocument()
  })

  it('shows the merge notice when the API reports a merged purchase', async () => {
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

    const notice = await screen.findByRole('status')
    expect(notice).toHaveTextContent(/merged/i)
    expect(notice).toHaveTextContent(/125/)

    await waitFor(() => expect(row().getByText('20')).toBeInTheDocument())
    expect(row().getAllByText('AAPL')).toHaveLength(1)
  })

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

    expect(editPanel().getByLabelText(/quantity/i)).toHaveValue(10)
    expect(editPanel().getByLabelText(/price/i)).toHaveValue(100)

    await user.clear(editPanel().getByLabelText(/quantity/i))
    await user.type(editPanel().getByLabelText(/quantity/i), '15')
    await user.clear(editPanel().getByLabelText(/price/i))
    await user.type(editPanel().getByLabelText(/price/i), '120')
    await user.click(editPanel().getByRole('button', { name: /^save$/i }))

    await waitFor(() => expect(row().getByText('15')).toBeInTheDocument())

    expect(row().getByText(/120/)).toBeInTheDocument()

    expect(patched).toEqual({ quantity: 15, price: 120 })

    await waitFor(() => expect(screen.queryByRole('form', { name: /correct/i })).not.toBeInTheDocument())
  })

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

    await waitFor(() => expect(row().getByText('15')).toBeInTheDocument())

    await waitFor(() => expect(row().getByText('10')).toBeInTheDocument(), { timeout: 500 })

    await waitFor(() => expect(queryClient.isFetching()).toBe(0), { timeout: 3000 })
    expect(await screen.findByRole('alert')).toHaveTextContent(/could not save the correction/i)

    expect(listCalls).toBe(1)
  })

  it('restores the row when an optimistic delete fails', async () => {
    let listCalls = 0

    server.use(
      http.delete('*/api/holdings/:id', async () => {
        await delay(150)
        return new HttpResponse(null, { status: 500 })
      }),
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

    await waitFor(() => expect(screen.queryByRole('table')).not.toBeInTheDocument())

    await waitFor(() => expect(row().getByText('AAPL')).toBeInTheDocument(), { timeout: 500 })

    await waitFor(() => expect(queryClient.isFetching()).toBe(0), { timeout: 3000 })
    expect(listCalls).toBe(1)

    expect(await screen.findByRole('alert')).toHaveTextContent(/could not remove aapl/i)
  })

  it('keeps the server own words when a delete is refused with a reason', async () => {
    server.use(
      http.delete('*/api/holdings/:id', () =>
        HttpResponse.json(
          { title: 'Service unavailable', detail: 'The database is not reachable.', status: 503 },
          { status: 503, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
      http.get('*/api/holdings', () => HttpResponse.json([AAPL])),
    )

    const user = userEvent.setup()
    await renderPortfolio()

    await user.click(row().getByRole('button', { name: /remove aapl/i }))
    await user.click(screen.getByRole('button', { name: /^remove$/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/the database is not reachable/i)
    expect(screen.getByRole('alert')).not.toHaveTextContent(/could not remove aapl/i)
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

    expect(await screen.findByText(/enter a valid ticker/i)).toBeInTheDocument()

    expect(posts).toBe(0)
  })

  it('keeps the shell and offers a retry when the holdings fetch fails', async () => {
    let calls = 0

    server.use(
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

    const router = createRouter({
      routeTree,
      history: createMemoryHistory({ initialEntries: ['/portfolio'] }),
      context: { queryClient, auth: authStore },
      defaultPreload: false,
    })

    render(<RouterProvider router={router as AnyRouter} />)

    expect(await screen.findByRole('heading', { name: 'Portfolio' })).toBeInTheDocument()
    expect(screen.getByRole('navigation', { name: 'Main' })).toBeInTheDocument()
    expect(await screen.findByRole('alert')).toHaveTextContent(/holdings are unavailable/i)

    await user.click(screen.getByRole('button', { name: /try again/i }))

    await waitFor(() => expect(row().getByText('AAPL')).toBeInTheDocument())
    expect(calls).toBe(2)
  })
})
