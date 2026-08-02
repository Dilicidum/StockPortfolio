import { afterEach, beforeEach, expect, it } from 'vitest'
import {
  requestSessionFromOtherTabs,
  SESSION_CHANNEL,
  startSessionSync,
  stopSessionSync,
} from '../src/auth/sessionChannel'
import { clearTokens, getRefreshToken, setTokens } from '../src/lib/tokenStore'

/**
 * The failure this prevents:
 *
 * The refresh token lives in sessionStorage, which is scoped to one tab. Open
 * the app in a second tab and it has no credential, so bootstrapSession finds
 * nothing, the guard in _authenticated.tsx sees isAuthenticated === false, and
 * the user lands on /login while still signed in next door.
 *
 * The fix is a handoff over BroadcastChannel: a new tab asks, a live tab
 * answers, and the token never touches persistent storage. These tests drive
 * both halves - asking and answering - plus the case that has no answer, which
 * must not hang the boot sequence.
 */

const laterToday = new Date(Date.now() + 15 * 60_000).toISOString()

beforeEach(() => {
  clearTokens()
})

afterEach(() => {
  stopSessionSync()
  clearTokens()
})

it('adopts a session offered by another tab', async () => {
  const peer = new BroadcastChannel(SESSION_CHANNEL)
  peer.onmessage = (event: MessageEvent) => {
    if (event.data?.kind === 'ask') {
      peer.postMessage({
        kind: 'offer',
        pair: { accessToken: 'peer-access', refreshToken: 'peer-refresh', accessExpiresAt: laterToday },
      })
    }
  }

  const pair = await requestSessionFromOtherTabs(500)
  peer.close()

  expect(pair?.refreshToken).toBe('peer-refresh')
  expect(pair?.accessToken).toBe('peer-access')
})

it('resolves null quickly when no other tab answers', async () => {
  const startedAt = Date.now()

  const pair = await requestSessionFromOtherTabs(150)

  expect(pair).toBeNull()
  // The boot sequence blocks on this before <RouterProvider> mounts, so a first
  // tab - the common case - must not sit on a splash screen waiting for nobody.
  expect(Date.now() - startedAt).toBeLessThan(1_000)
})

it('answers a tab that asks, once it is serving', async () => {
  setTokens({ accessToken: 'mine-access', refreshToken: 'mine-refresh', accessExpiresAt: laterToday })
  startSessionSync()

  const asker = new BroadcastChannel(SESSION_CHANNEL)
  const offered = new Promise<Record<string, unknown>>((resolve) => {
    asker.onmessage = (event: MessageEvent) => {
      if (event.data?.kind === 'offer') resolve(event.data.pair)
    }
  })

  asker.postMessage({ kind: 'ask' })
  const pair = await offered
  asker.close()

  expect(pair).toMatchObject({ accessToken: 'mine-access', refreshToken: 'mine-refresh' })
})

it('does not answer when it has no session to give', async () => {
  startSessionSync()

  const asker = new BroadcastChannel(SESSION_CHANNEL)
  let answered = false
  asker.onmessage = (event: MessageEvent) => {
    if (event.data?.kind === 'offer') answered = true
  }

  asker.postMessage({ kind: 'ask' })
  await new Promise((resolve) => setTimeout(resolve, 100))
  asker.close()

  // A signed-out tab offering an empty session would hand the asker a null
  // credential and stop it falling through to the ordinary /login path.
  expect(answered).toBe(false)
})

it('adopts a rotation broadcast by another tab', async () => {
  setTokens({ accessToken: 'old-access', refreshToken: 'old-refresh', accessExpiresAt: laterToday })
  startSessionSync()

  const peer = new BroadcastChannel(SESSION_CHANNEL)
  peer.postMessage({
    kind: 'offer',
    pair: { accessToken: 'new-access', refreshToken: 'new-refresh', accessExpiresAt: laterToday },
  })
  await new Promise((resolve) => setTimeout(resolve, 50))
  peer.close()

  // Without this, the tab that did NOT rotate keeps a superseded refresh token
  // and is logged out the moment the server's rotation grace period lapses -
  // a bug that only shows up 30 seconds later, in whichever tab you were not
  // looking at.
  expect(getRefreshToken()).toBe('new-refresh')
})
