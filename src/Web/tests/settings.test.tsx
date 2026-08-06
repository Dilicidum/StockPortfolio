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

// The QueryClient is a module singleton shared by every test FILE in the run.
beforeEach(() => {
  authStore.signOut()
  queryClient.clear()
  __resetRefreshInFlight()
})

/**
 * The fifth inline copy of the memory-router boilerplate (`portfolio.test.tsx`,
 * `dashboard.test.tsx`, `auth.test.tsx` and `sessionPersistence.test.tsx` are the first four),
 * following the same convention. Every section fires its own GET on mount — five requests —
 * plus the layout's alert stubs, so `defaultSettingsHandlers` and `alertsHandlers` both go in
 * before anything else so a test that wants its own behaviour can still shadow one with
 * `server.use(...)`, which wins because MSW resolves the most recently added match.
 */
async function renderSettings(holdings: Holding[] = [AAPL, TSLA]) {
  authStore.setUser({ email: 'holder@example.com' })
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

/** Each `Card` is a `<section>` with an `<h2>` title, so this scopes a query to one section. */
const sectionFor = (heading: string | RegExp) =>
  within(screen.getByRole('heading', { name: heading }).closest('section') as HTMLElement)

describe('settings', () => {
  it('savingTheTheme_WhenTheApiKeySectionIsFailing_StillSaves', async () => {
    server.use(saveApiKeyRejectedHandler)

    const user = userEvent.setup()
    await renderSettings()

    // The API key section fails first...
    await user.type(sectionFor('Your own API key').getByLabelText(/api key/i), 'a-bad-key-value')
    await user.click(sectionFor('Your own API key').getByRole('button', { name: /save key/i }))
    expect(await sectionFor('Your own API key').findByText(/rejected this key/i)).toBeInTheDocument()

    // ...and the theme still saves, because each section is its own PUT. One form with one
    // Save button would let the rejected key throw the theme change away with it.
    await user.selectOptions(sectionFor('Appearance').getByLabelText(/theme/i), 'dark')
    await user.click(sectionFor('Appearance').getByRole('button', { name: /^save$/i }))

    expect(await sectionFor('Appearance').findByText('Saved')).toBeInTheDocument()
    // The API key section's failure is still showing — saving elsewhere did not clear it.
    expect(sectionFor('Your own API key').getByText(/rejected this key/i)).toBeInTheDocument()
  })

  it('hidingAPosition_UpdatesTheCounterAndTheDashboard', async () => {
    // Seeded so `invalidateQueries` (the dashboard's own cache key, separate from
    // `/api/holdings`) has something in the cache to mark stale.
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

    // The dashboard's own query is a DIFFERENT key from `/api/holdings` and filters visibility
    // server-side, so hiding a position here has to invalidate it too or the dashboard would
    // keep showing a row just hidden from this screen.
    await waitFor(() =>
      expect(queryClient.getQueryState(dashboardKeys.view())?.isInvalidated).toBe(true),
    )
  })

  /**
   * THE ONE THAT CATCHES THE RACY LOOP. Firing `setVisibility.mutate(...)` once per hidden
   * holding without awaiting let every call's `onMutate` snapshot the SAME pre-loop list, so
   * one PATCH failing rolled back siblings that had already succeeded — undoing a toggle the
   * user never touched. MSFT's PATCH is made to fail here; TSLA's must still end up visible.
   */
  it('showAll_WhenOnePatchFails_RevertsOnlyThatOne', async () => {
    const user = userEvent.setup()
    await renderSettings([AAPL, TSLA_HIDDEN, MSFT_HIDDEN])

    // Registered after mount, so it wins over `defaultSettingsHandlers`' blanket success
    // handler for every PATCH issued from here on — exactly the convention this file's own
    // comment on `defaultSettingsHandlers` describes.
    server.use(setHoldingVisibilityFailingFor(MSFT_HIDDEN.id))

    await user.click(sectionFor('Visible positions').getByRole('button', { name: /show all/i }))

    await waitFor(() => {
      expect(sectionFor('Visible positions').getByRole('checkbox', { name: /toggle tsla/i })).toBeChecked()
    })
    // The one that actually failed reverted; a passing implementation of the old loop would
    // have also un-checked TSLA here, because its rollback restored the whole pre-loop list.
    expect(sectionFor('Visible positions').getByRole('checkbox', { name: /toggle msft/i })).not.toBeChecked()
    expect(await sectionFor('Visible positions').findByText(/could not show/i)).toBeInTheDocument()
  })

  /**
   * GET /api/settings/api-key returning 404 means bring-your-own-key is switched off on this
   * deployment. Before this fix `data` merely stayed `undefined` and the form rendered as
   * usual — the user only learned the feature was off after typing a key and pressing Save.
   */
  it('apiKeySection_WhenTheFeatureIsDisabled_DoesNotRenderAWorkingForm', async () => {
    authStore.setUser({ email: 'holder@example.com' })
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
