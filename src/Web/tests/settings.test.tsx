import { beforeEach, describe, expect, it } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
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
import { dashboardKeys, type GetDashboardResult } from '../src/marketdata/dashboardApi'
import { holdingKeys, type Holding } from '../src/portfolio/holdingsApi'
import { alertsHandlers } from './msw/alerts'
import {
  apiKeyStatusUnavailableHandler,
  dashboardSettingsHandler,
  defaultSettingsHandlers,
  saveApiKeyRejectedHandler,
  saveAppearanceHandler,
  saveDashboardSettingsHandler,
  setHoldingVisibilityFailingFor,
  setHoldingVisibilityHandler,
} from './msw/settings'
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

const TSLA: Holding = {
  id: '0199a1f0-0000-7000-8000-000000000002',
  ticker: 'TSLA',
  name: null,
  quantity: 5,
  averagePrice: { amount: '200', currency: 'USD' },
  invested: { amount: '1000', currency: 'USD' },
  isVisible: true,
  updatedAt: '2026-08-04T12:00:00+00:00',
}

const TSLA_HIDDEN: Holding = { ...TSLA, isVisible: false }

const MSFT_HIDDEN: Holding = {
  id: '0199a1f0-0000-7000-8000-000000000003',
  ticker: 'MSFT',
  name: null,
  quantity: 3,
  averagePrice: { amount: '300', currency: 'USD' },
  invested: { amount: '900', currency: 'USD' },
  isVisible: false,
  updatedAt: '2026-08-04T12:00:00+00:00',
}

beforeEach(() => {
  authStore.signOut()
  queryClient.clear()
  __resetRefreshInFlight()
})

async function renderSettings(holdings: Holding[] = [AAPL, TSLA]) {
  authStore.setUser({ id: 'u-1', email: 'holder@example.com' })
  queryClient.setQueryData(holdingKeys.list(), holdings)

  server.use(...alertsHandlers, ...defaultSettingsHandlers)

  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ['/settings'] }),
    context: { queryClient, auth: authStore },
    defaultPreload: false,
  })

  render(<RouterProvider router={router as AnyRouter} />)
  await screen.findByRole('heading', { name: 'Settings' })

  return router
}

const sectionFor = (heading: string | RegExp) =>
  within(screen.getByRole('heading', { name: heading }).closest('section') as HTMLElement)

describe('settings', () => {
  it('savingTheTheme_WhenTheApiKeySectionIsFailing_StillSaves', async () => {
    server.use(saveApiKeyRejectedHandler)

    const user = userEvent.setup()
    await renderSettings()

    await user.type(sectionFor('Your own API key').getByLabelText(/api key/i), 'a-bad-key-value')
    await user.click(sectionFor('Your own API key').getByRole('button', { name: /save key/i }))
    expect(await sectionFor('Your own API key').findByText(/rejected this key/i)).toBeInTheDocument()

    await user.selectOptions(sectionFor('Appearance').getByLabelText(/theme/i), 'dark')
    await user.click(sectionFor('Appearance').getByRole('button', { name: /^save$/i }))

    expect(await sectionFor('Appearance').findByText('Saved')).toBeInTheDocument()
    expect(sectionFor('Your own API key').getByText(/rejected this key/i)).toBeInTheDocument()
  })

  it('hidingAPosition_UpdatesTheCounterAndTheDashboard', async () => {
    queryClient.setQueryData<GetDashboardResult>(dashboardKeys.view(), {
      positions: [],
      totals: {
        value: { amount: '0', currency: 'USD' },
        cost: { amount: '0', currency: 'USD' },
        profit: { amount: '0', currency: 'USD' },
        profitPercent: null,
        positionCount: 0,
        pricedPositionCount: 0,
      },
      asOf: '2026-08-06T12:00:00+00:00',
      stalestObservedAt: null,
    })

    const user = userEvent.setup()
    await renderSettings()

    expect(sectionFor('Visible positions').getByText('Showing 2 of 2')).toBeInTheDocument()

    await user.click(sectionFor('Visible positions').getByRole('checkbox', { name: /toggle aapl/i }))

    await waitFor(() =>
      expect(sectionFor('Visible positions').getByText('Showing 1 of 2')).toBeInTheDocument(),
    )

    await waitFor(() =>
      expect(queryClient.getQueryState(dashboardKeys.view())?.isInvalidated).toBe(true),
    )
  })

  it('showAll_WhenOnePatchFails_RevertsOnlyThatOne', async () => {
    const user = userEvent.setup()
    await renderSettings([AAPL, TSLA_HIDDEN, MSFT_HIDDEN])

    server.use(setHoldingVisibilityFailingFor(MSFT_HIDDEN.id))

    await user.click(sectionFor('Visible positions').getByRole('button', { name: /show all/i }))

    await waitFor(() => {
      expect(sectionFor('Visible positions').getByRole('checkbox', { name: /toggle tsla/i })).toBeChecked()
    })
    expect(sectionFor('Visible positions').getByRole('checkbox', { name: /toggle msft/i })).not.toBeChecked()
    expect(await sectionFor('Visible positions').findByText(/could not show/i)).toBeInTheDocument()
  })

  it('apiKeySection_WhenTheFeatureIsDisabled_DoesNotRenderAWorkingForm', async () => {
    authStore.setUser({ id: 'u-1', email: 'holder@example.com' })
    queryClient.setQueryData(holdingKeys.list(), [AAPL, TSLA])

    server.use(
      ...alertsHandlers,
      dashboardSettingsHandler(),
      saveDashboardSettingsHandler,
      saveAppearanceHandler,
      apiKeyStatusUnavailableHandler,
      setHoldingVisibilityHandler(),
    )

    const router = createRouter({
      routeTree,
      history: createMemoryHistory({ initialEntries: ['/settings'] }),
      context: { queryClient, auth: authStore },
      defaultPreload: false,
    })

    render(<RouterProvider router={router as AnyRouter} />)
    await screen.findByRole('heading', { name: 'Settings' })

    expect(await sectionFor('Your own API key').findByText(/not available/i)).toBeInTheDocument()
    expect(sectionFor('Your own API key').queryByLabelText(/api key/i)).not.toBeInTheDocument()
    expect(sectionFor('Your own API key').queryByRole('button', { name: /save key/i })).not.toBeInTheDocument()
  })
})
