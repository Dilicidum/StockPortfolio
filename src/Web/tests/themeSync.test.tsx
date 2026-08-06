import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
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
import { appearanceHandler } from './msw/appearance'
import { emptyTickerSearchHandler } from './msw/tickerSearch'
import { server } from './msw/server'

const THEME_STORAGE_KEY = 'stockportfolio.theme'

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

/** Stands in for window.matchMedia, exactly as `theme.test.tsx` does — jsdom has none. */
class StubMediaQueryList {
  matches: boolean
  private listeners = new Set<(event: MediaQueryListEvent) => void>()

  constructor(matches: boolean) {
    this.matches = matches
  }

  addEventListener(_type: 'change', listener: (event: MediaQueryListEvent) => void): void {
    this.listeners.add(listener)
  }

  removeEventListener(_type: 'change', listener: (event: MediaQueryListEvent) => void): void {
    this.listeners.delete(listener)
  }

  dispatch(matches: boolean): void {
    this.matches = matches
    for (const listener of this.listeners) listener({ matches } as MediaQueryListEvent)
  }
}

let stub: StubMediaQueryList

beforeEach(() => {
  authStore.signOut()
  queryClient.clear()
  __resetRefreshInFlight()
  localStorage.removeItem(THEME_STORAGE_KEY)
  stub = new StubMediaQueryList(false)
  vi.stubGlobal('matchMedia', vi.fn(() => stub))
})

afterEach(() => {
  vi.unstubAllGlobals()
  localStorage.removeItem(THEME_STORAGE_KEY)
  document.documentElement.removeAttribute('data-theme')
  document.documentElement.style.colorScheme = ''
})

/** Same boilerplate as `i18n.test.tsx`'s `renderPortfolio` — any authenticated route does, since the theme sync lives in the layout, not the Settings screen. */
async function renderPortfolio() {
  authStore.setUser({ id: 'u-1', email: 'holder@example.com' })
  queryClient.setQueryData(holdingKeys.list(), [AAPL])
  server.use(emptyTickerSearchHandler, ...alertsHandlers)

  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ['/portfolio'] }),
    context: { queryClient, auth: authStore },
    defaultPreload: false,
  })

  render(<RouterProvider router={router as AnyRouter} />)
  await screen.findByRole('table')

  return router
}

describe('useSyncServerTheme', () => {
  /**
   * THE ONE THAT PROVES SIGN-IN ON A SECOND DEVICE WORKS. No cache write here — a fresh
   * browser with nothing in localStorage — so this starts exactly like a first-time visitor,
   * defaulting to 'system'. The server disagrees and holds 'dark'; the page has to move to
   * match it without anyone opening Settings.
   */
  it('serverTheme_DisagreeingWithTheCache_Wins', async () => {
    server.use(appearanceHandler({ theme: 'dark', language: 'en' }))

    await renderPortfolio()

    await waitFor(() => {
      expect(document.documentElement.getAttribute('data-theme')).toBe('dark')
    })
  })

  /**
   * THE STRICTMODE-SURVIVING LIVE-FOLLOW CASE. `watchSystemTheme` already had a correct
   * teardown and its own unit tests before this — the defect was that nothing ever called
   * it. The OS flips mid-session, with nobody touching Settings, and the page has to move.
   */
  it('systemTheme_ChangingWhileChoiceIsSystem_UpdatesLive', async () => {
    server.use(appearanceHandler({ theme: 'system', language: 'en' }))

    await renderPortfolio()

    await waitFor(() => {
      expect(document.documentElement.getAttribute('data-theme')).toBe('light')
    })

    stub.dispatch(true)

    await waitFor(() => {
      expect(document.documentElement.getAttribute('data-theme')).toBe('dark')
    })
  })

  /** The choice is 'dark', not 'system' — an OS flip must not move a page that opted out of following it. */
  it('systemTheme_ChangingWhileChoiceIsDark_DoesNothing', async () => {
    server.use(appearanceHandler({ theme: 'dark', language: 'en' }))

    await renderPortfolio()

    await waitFor(() => {
      expect(document.documentElement.getAttribute('data-theme')).toBe('dark')
    })

    // The DOM attribute flips to 'dark' in the same commit that STARTS tearing the system
    // listener down (both follow from the one `setChoice('dark')` call), but the teardown
    // itself lands in the render that call schedules — the same tick in a real browser, one
    // beat later under this harness's scheduler. Give it that beat before proving silence.
    await new Promise((resolve) => setTimeout(resolve, 10))

    stub.dispatch(false)

    expect(document.documentElement.getAttribute('data-theme')).toBe('dark')
  })
})
