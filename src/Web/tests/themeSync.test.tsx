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
  it('serverTheme_DisagreeingWithTheCache_Wins', async () => {
    server.use(appearanceHandler({ theme: 'dark', language: 'en' }))

    await renderPortfolio()

    await waitFor(() => {
      expect(document.documentElement.getAttribute('data-theme')).toBe('dark')
    })
  })

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

  it('systemTheme_ChangingWhileChoiceIsDark_DoesNothing', async () => {
    server.use(appearanceHandler({ theme: 'dark', language: 'en' }))

    await renderPortfolio()

    await waitFor(() => {
      expect(document.documentElement.getAttribute('data-theme')).toBe('dark')
    })

    await new Promise((resolve) => setTimeout(resolve, 10))

    stub.dispatch(false)

    expect(document.documentElement.getAttribute('data-theme')).toBe('dark')
  })
})
