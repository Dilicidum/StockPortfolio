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

// Every test here either calls `applyServerLanguage` directly or mounts a route that
// eventually does, by design (`useSyncServerLanguage`). Reset the shared `i18n` singleton
// back to English so a language switch in one test cannot leak into the next `it()` in this
// file — other test files are unaffected, since each gets its own fresh jsdom environment.
afterEach(() => {
  if (i18n.language !== 'en') void i18n.changeLanguage('en')
  localStorage.removeItem(LANGUAGE_STORAGE_KEY)
})

/**
 * The sixth inline copy of the memory-router boilerplate (see portfolio.test.tsx's comment
 * on the third). Portfolio is used here rather than dashboard because it is the one route
 * that renders both a translated nav and a translated `<Table>` with real column headers —
 * an empty dashboard's `Table` returns its empty-state div before any `<thead>` exists.
 */
async function renderPortfolio() {
  authStore.setUser({ email: 'holder@example.com' })
  queryClient.setQueryData(holdingKeys.list(), [AAPL])
  server.use(emptyTickerSearchHandler, ...alertsHandlers)

  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ['/portfolio'] }),
    context: { queryClient, auth: authStore },
    defaultPreload: false,
  })

  render(<RouterProvider router={router as AnyRouter} />)
  // Language-agnostic on purpose: some callers render already switched to Ukrainian, so
  // waiting on the English heading text specifically would never resolve for them.
  // `findByText('AAPL')` would ambiguously match twice — `Table` renders both the desktop
  // table and the mobile card list into the DOM at every width, per its own comment — so
  // this waits on the table role instead.
  await screen.findByRole('table')

  return router
}

describe('i18n', () => {
  it('switchingLanguage_ToUkrainian_TranslatesNavigationAndTableHeaders', async () => {
    // Matches the language about to be applied, so `useSyncServerLanguage`'s own request —
    // still in flight when the switch below happens — cannot resolve a moment later and
    // silently flip this back to English out from under the assertion.
    server.use(appearanceHandler({ theme: 'system', language: 'uk' }))
    await renderPortfolio()

    // English by default — nothing in this file has switched languages yet.
    expect(screen.getByRole('link', { name: 'Portfolio' })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: 'Asset' })).toBeInTheDocument()

    await applyServerLanguage('uk')

    // Both the nav link (common.json) and the table header (portfolio.json) come from
    // different namespaces, so this also proves both loaded, not just one.
    expect(await screen.findByRole('link', { name: 'Портфель' })).toBeInTheDocument()
    expect(await screen.findByRole('columnheader', { name: 'Актив' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Portfolio' })).not.toBeInTheDocument()
  })

  /**
   * THE ONE THAT PROVES THE CONVENTION ACTUALLY REACHES THE USER. Three forms already used
   * "errors.foo.bar" message keys before this task; the defect the brief opens with is that
   * they rendered to the user as that literal key path. Submitting an invalid form in
   * Ukrainian and reading Ukrainian text back — not the raw key, and not English — is what
   * proves `translateFieldError` actually closed that gap rather than just moving it.
   */
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
    // Simulates a earlier tab having chosen Ukrainian and the browser being closed —
    // nothing in this test calls `applyServerLanguage` itself.
    localStorage.setItem(LANGUAGE_STORAGE_KEY, 'uk')

    // `vi.resetModules()` clears the module registry so the next `import()` re-runs
    // lib/i18n.ts's top-level `i18n.init(...)` from scratch, exactly as a real page reload
    // re-executes the whole bundle. Without this, "reload" would just mean "read the
    // variable this same module already computed once" and could not fail.
    vi.resetModules()
    const reloaded = await import('../src/lib/i18n')

    expect(reloaded.readCachedLanguage()).toBe('uk')
    expect(reloaded.default.language).toBe('uk')
  })

  /**
   * The failure that only shows up on the SECOND page load: a returning user's browser
   * cache disagrees with what they actually chose (a different browser, a cleared cache, or
   * simply never having visited before). The pre-sign-in guess must lose the moment a real
   * session answers.
   */
  it('serverLanguage_DisagreeingWithTheCache_Wins', async () => {
    // No cache write here — `readCachedLanguage()` falls back to 'en', so this starts
    // exactly like a first-time visitor. The server disagrees.
    server.use(appearanceHandler({ theme: 'system', language: 'uk' }))

    await renderPortfolio()

    expect(screen.getByRole('link', { name: 'Portfolio' })).toBeInTheDocument()

    await waitFor(() => {
      expect(screen.getByRole('link', { name: 'Портфель' })).toBeInTheDocument()
    })
    expect(screen.queryByRole('link', { name: 'Portfolio' })).not.toBeInTheDocument()
  })
})
