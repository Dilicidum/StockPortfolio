import '@testing-library/jest-dom/vitest'
import { afterAll, afterEach, beforeAll } from 'vitest'
import { cleanup } from '@testing-library/react'
import { server } from './msw/server'

// jsdom has no layout, so it logs "Not implemented: Window's scrollTo()" every
// time the router restores scroll position. Stub it rather than read past it.
window.scrollTo = (() => {}) as typeof window.scrollTo

// `error` rather than the default `warn`: an unhandled request means a test is
// hitting the network by accident, which is exactly the kind of thing that
// passes locally and hangs in CI.
beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))

afterEach(() => {
  cleanup()
  server.resetHandlers()
})

afterAll(() => server.close())
