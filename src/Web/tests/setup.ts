import '@testing-library/jest-dom/vitest'
import { afterAll, afterEach, beforeAll, beforeEach } from 'vitest'
import { cleanup } from '@testing-library/react'
import { FakeEventSource, installFakeEventSource } from './fakeEventSource'
import { __resetAlertStream } from '../src/alerts/useAlertStream'
import { defaultAppearanceHandler } from './msw/appearance'
import { server } from './msw/server'

// jsdom has no layout, so it logs "Not implemented: Window's scrollTo()" every
// time the router restores scroll position. Stub it rather than read past it.
window.scrollTo = (() => {}) as typeof window.scrollTo

// jsdom implements no EventSource either, and unlike scrollTo its absence is fatal:
// the authenticated layout opens the alert stream, so every protected route would
// throw on mount. See tests/fakeEventSource.ts.
installFakeEventSource()

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
  FakeEventSource.reset()
  __resetAlertStream()
})

afterAll(() => server.close())
