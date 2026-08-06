import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { applyTheme, watchSystemTheme } from '../src/lib/theme'

/** Stands in for window.matchMedia, which jsdom does not implement. */
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
  stub = new StubMediaQueryList(false)
  vi.stubGlobal('matchMedia', vi.fn(() => stub))
})

afterEach(() => {
  vi.unstubAllGlobals()
  document.documentElement.removeAttribute('data-theme')
  document.documentElement.style.colorScheme = ''
})

it('applyTheme_WithDark_SetsTheDocumentAttribute', () => {
  applyTheme('dark')

  expect(document.documentElement.getAttribute('data-theme')).toBe('dark')
  expect(document.documentElement.style.colorScheme).toBe('dark')
})

it('applyTheme_WithSystem_FollowsTheMediaQuery', () => {
  stub.matches = true
  applyTheme('system')
  expect(document.documentElement.getAttribute('data-theme')).toBe('dark')

  stub.matches = false
  applyTheme('system')
  expect(document.documentElement.getAttribute('data-theme')).toBe('light')
})

it('watchSystemTheme_WhenTheOsThemeChanges_CallsBack', () => {
  const onChange = vi.fn()
  const teardown = watchSystemTheme(onChange)

  stub.dispatch(true)

  expect(onChange).toHaveBeenCalledTimes(1)
  teardown()
})

/**
 * The StrictMode case. React 19 mounts an effect, tears it down, and mounts it again; a
 * watcher that does not remove its own listener in teardown leaves two registered against
 * one media query. This is the only test that goes red on a missing removeEventListener.
 */
it('watchSystemTheme_AfterTeardown_DoesNotCallBack', () => {
  const onChange = vi.fn()
  const teardown = watchSystemTheme(onChange)

  teardown()
  stub.dispatch(true)

  expect(onChange).not.toHaveBeenCalled()
})
