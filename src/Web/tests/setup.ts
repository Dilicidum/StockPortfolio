import '@testing-library/jest-dom/vitest'
import { afterAll, afterEach, beforeAll, beforeEach, vi } from 'vitest'
import { cleanup } from '@testing-library/react'
import { FakeHubConnection } from './fakeHubConnection'
import { __resetAlertStream } from '../src/alerts/useAlertStream'
import { defaultAppearanceHandler } from './msw/appearance'
import { server } from './msw/server'

// jsdom has no layout, so it logs "Not implemented: Window's scrollTo()" every
// time the router restores scroll position. Stub it rather than read past it.
window.scrollTo = (() => {}) as typeof window.scrollTo

// jsdom has no matchMedia either, and every authenticated route now watches it continuously
// (useSyncServerTheme, for "Match system"). A query that never matches and never fires is a
// safe default for every test that does not care; theme.test.tsx installs its own stub via
// `vi.stubGlobal` for the tests that do, which wins over this one and is undone by
// `vi.unstubAllGlobals()` back to exactly this baseline.
if (!window.matchMedia) {
  window.matchMedia = ((query: string) => ({
    matches: false,
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {},
  })) as unknown as typeof window.matchMedia
}

// The authenticated layout opens the alert connection on mount, and jsdom's WebSocket reaches
// nothing — so without this every protected-route test would hang rather than fail. Registered
// here rather than per file, because it applies everywhere. See tests/fakeHubConnection.ts.
vi.mock('@microsoft/signalr', async () => (await import('./fakeHubConnection')).signalRModuleMock)

// `error` rather than the default `warn`: an unhandled request means a test is
// hitting the network by accident, which is exactly the kind of thing that
// passes locally and hangs in CI.
beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))

// `_authenticated.tsx` now calls `useSyncServerLanguage`, which fires GET
// /api/settings/appearance on mount alongside the alert stream's requests — so, like those,
// every test that merely mounts a protected route needs a handler for it. Registered here
// rather than added to every test file's own `server.use(...)`, exactly because it applies
// everywhere; a test that cares about a specific language overrides it with its own
// `server.use`, which wins because MSW resolves the most recently added matching handler.
beforeEach(() => server.use(defaultAppearanceHandler))

afterEach(() => {
  cleanup()
  server.resetHandlers()

  // `cleanup()` unmounts the layout, which closes the connection; these two clear what it
  // left behind. The stream's "one connection" flag is a module singleton, so a test that
  // somehow skipped its own cleanup would otherwise stop the next file connecting at all.
  FakeHubConnection.reset()
  __resetAlertStream()
})

afterAll(() => server.close())
