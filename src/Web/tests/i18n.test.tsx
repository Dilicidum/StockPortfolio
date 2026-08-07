import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
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
import i18n, { applyServerLanguage } from '../src/lib/i18n'
import { holdingKeys, type Holding } from '../src/portfolio/holdingsApi'
import { alertsHandlers } from './msw/alerts'
import { appearanceHandler } from './msw/appearance'
import { emptyTickerSearchHandler } from './msw/tickerSearch'
import { server } from './msw/server'

const LANGUAGE_STORAGE_KEY = 'stockportfolio.language'

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
  localStorage.removeItem(LANGUAGE_STORAGE_KEY)
})

afterEach(() => {
  if (i18n.language !== 'en') void i18n.changeLanguage('en')
  localStorage.removeItem(LANGUAGE_STORAGE_KEY)
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

describe('i18n', () => {
  it('switchingLanguage_ToUkrainian_TranslatesNavigationAndTableHeaders', async () => {
    server.use(appearanceHandler({ theme: 'system', language: 'uk' }))
    await renderPortfolio()

    expect(screen.getByRole('link', { name: 'Portfolio' })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: 'Asset' })).toBeInTheDocument()

    await applyServerLanguage('uk')

    expect(await screen.findByRole('link', { name: 'Портфель' })).toBeInTheDocument()
    expect(await screen.findByRole('columnheader', { name: 'Актив' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Portfolio' })).not.toBeInTheDocument()
  })

  it('switchingLanguage_ToUkrainian_TranslatesAValidationMessage', async () => {
    server.use(appearanceHandler({ theme: 'system', language: 'uk' }))
    await applyServerLanguage('uk')
    const user = userEvent.setup()
    await renderPortfolio()

    await user.type(screen.getByLabelText('Тікер'), 'TOOLONG')
    await user.type(screen.getByLabelText('Кількість'), '1')
    await user.type(screen.getByLabelText('Ціна купівлі'), '1')
    await user.click(screen.getByRole('button', { name: 'Додати позицію' }))

    expect(await screen.findByText('Введіть правильний тікер (1–5 літер).')).toBeInTheDocument()
    expect(screen.queryByText('errors.ticker.format')).not.toBeInTheDocument()
  })

  it('reload_AfterChoosingUkrainian_StaysUkrainian', async () => {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, 'uk')

    vi.resetModules()
    const reloaded = await import('../src/lib/i18n')

    expect(reloaded.readCachedLanguage()).toBe('uk')
    expect(reloaded.default.language).toBe('uk')
  })

  it('serverLanguage_DisagreeingWithTheCache_Wins', async () => {
    server.use(appearanceHandler({ theme: 'system', language: 'uk' }))

    await renderPortfolio()

    expect(screen.getByRole('link', { name: 'Portfolio' })).toBeInTheDocument()

    await waitFor(() => {
      expect(screen.getByRole('link', { name: 'Портфель' })).toBeInTheDocument()
    })
    expect(screen.queryByRole('link', { name: 'Portfolio' })).not.toBeInTheDocument()
  })
})
