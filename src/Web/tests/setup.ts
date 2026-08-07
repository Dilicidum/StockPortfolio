import '@testing-library/jest-dom/vitest'
import { afterAll, afterEach, beforeAll, beforeEach, vi } from 'vitest'
import { cleanup } from '@testing-library/react'
import { FakeHubConnection } from './fakeHubConnection'
import { __resetAlertStream } from '../src/alerts/useAlertStream'
import { defaultAppearanceHandler } from './msw/appearance'
import { server } from './msw/server'

window.scrollTo = (() => {}) as typeof window.scrollTo

if (!window.matchMedia) {
  window.matchMedia = ((query: string) => ({
    matches: false,
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {},
  })) as unknown as typeof window.matchMedia
}

vi.mock('@microsoft/signalr', async () => (await import('./fakeHubConnection')).signalRModuleMock)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))

beforeEach(() => server.use(defaultAppearanceHandler))

afterEach(() => {
  cleanup()
  server.resetHandlers()

  FakeHubConnection.reset()
  __resetAlertStream()
})

afterAll(() => server.close())
