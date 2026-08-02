import {
  getAccessExpiresAt,
  getAccessToken,
  getRefreshToken,
  setTokens,
  setTokensChangedListener,
  type TokenPair,
} from '../lib/tokenStore'

/**
 * CROSS-TAB SESSION HANDOFF — why this exists and why it is not storage.
 *
 * The refresh token lives in sessionStorage, which is scoped to a single tab.
 * That is deliberate: nothing durable is written to disk, so an XSS cannot walk
 * storage once and leave with a fortnight-long credential. The cost is that a
 * second tab starts with no credential at all and bounces to /login while the
 * first tab is still signed in.
 *
 * The obvious fixes were all rejected on purpose:
 *
 *   localStorage      shared, but persists a 14-day refresh token where any
 *                     injected script can read it in one line.
 *   httpOnly cookie   the correct answer, and unavailable: the SPA is on
 *                     github.io and the API on Azure Container Apps, so the
 *                     cookie would be third-party. Safari blocks those outright.
 *
 * So this module moves the token between tabs instead of storing it anywhere
 * new. BroadcastChannel is a message bus, not storage - nothing is persisted,
 * and a tab that is not running cannot be asked. Closing every tab still ends
 * the session, exactly as the login page promises.
 *
 * TWO JOBS, and the second is not optional:
 *
 *   1. A new tab ASKS and a live tab OFFERS. That is the reported bug.
 *   2. Every rotation is broadcast to the other tabs.
 *
 * Without (2), the handoff creates a worse bug than it fixes. Refresh tokens
 * are single-use: the new tab spends the one it was given, the server issues a
 * replacement, and the tab that donated it is now holding a superseded token.
 * The server's 30-second RotationGracePeriod hides this briefly, then the
 * donating tab is silently logged out - 30 seconds later, in whichever tab the
 * user was not looking at.
 *
 * KNOWN GAP: signing out in one tab does not immediately sign out the others.
 * Because the tabs now share one server-side session, logout revokes the shared
 * refresh token, so the others drop on their next refresh - within one
 * access-token lifetime rather than instantly. Propagating it is a small
 * addition, but it needs to reach authStore to update the UI, so it is left out
 * rather than half-done.
 */

export const SESSION_CHANNEL = 'stockportfolio.session'

type Ask = { kind: 'ask' }
type Offer = { kind: 'offer'; pair: TokenPair }
type SessionMessage = Ask | Offer

let channel: BroadcastChannel | null = null

/** Guards the echo: a token adopted from another tab must not be re-broadcast. */
let applyingRemote = false

/** Absent in older Safari and in some test environments — never let it throw. */
function openChannel(): BroadcastChannel | null {
  try {
    if (typeof BroadcastChannel === 'undefined') return null
    return new BroadcastChannel(SESSION_CHANNEL)
  } catch {
    return null
  }
}

/**
 * The full triple, or null. A partial session is never offered: handing a peer
 * an access token with no refresh token would leave it authenticated for
 * fifteen minutes and then unable to recover, which is worse than the honest
 * redirect to /login it would otherwise have taken.
 */
function currentPair(): TokenPair | null {
  const accessToken = getAccessToken()
  const accessExpiresAt = getAccessExpiresAt()
  const refreshToken = getRefreshToken()

  if (!accessToken || !accessExpiresAt || !refreshToken) return null

  return { accessToken, accessExpiresAt, refreshToken }
}

/** Starts answering other tabs and mirroring local rotations to them. */
export function startSessionSync(): void {
  if (channel) return

  channel = openChannel()
  if (!channel) return

  channel.onmessage = (event: MessageEvent<SessionMessage>) => {
    const message = event.data
    if (!message) return

    if (message.kind === 'ask') {
      const pair = currentPair()
      if (pair) channel?.postMessage({ kind: 'offer', pair } satisfies Offer)
      return
    }

    if (message.kind === 'offer' && message.pair?.refreshToken) {
      applyingRemote = true
      try {
        setTokens(message.pair)
      } finally {
        applyingRemote = false
      }
    }
  }

  setTokensChangedListener((pair) => {
    if (applyingRemote) return
    if (pair?.refreshToken) channel?.postMessage({ kind: 'offer', pair } satisfies Offer)
  })
}

/**
 * Stores a pair received from another tab without echoing it straight back.
 * Re-broadcasting would be harmless in value — the tokens are identical — but
 * it puts a pointless round of messages on the bus on every new tab.
 */
export function adoptRemoteTokens(pair: TokenPair): void {
  applyingRemote = true
  try {
    setTokens(pair)
  } finally {
    applyingRemote = false
  }
}

export function stopSessionSync(): void {
  setTokensChangedListener(null)
  channel?.close()
  channel = null
}

/**
 * Asks the other tabs for a session, resolving null if none answers in time.
 *
 * The timeout is short and load-bearing. bootstrapSession awaits this before
 * <RouterProvider> mounts, so every millisecond here is splash screen for the
 * common case of a first tab with nobody to ask. A channel never delivers a
 * message to itself, so a lone tab always takes the timeout - it cannot be
 * shortened by looking for our own message coming back.
 */
export function requestSessionFromOtherTabs(timeoutMs = 250): Promise<TokenPair | null> {
  const asking = openChannel()
  if (!asking) return Promise.resolve(null)

  return new Promise((resolve) => {
    let settled = false

    const finish = (pair: TokenPair | null) => {
      if (settled) return
      settled = true
      clearTimeout(timer)
      asking.close()
      resolve(pair)
    }

    const timer = setTimeout(() => finish(null), timeoutMs)

    asking.onmessage = (event: MessageEvent<SessionMessage>) => {
      const message = event.data
      if (message?.kind === 'offer' && message.pair?.refreshToken) finish(message.pair)
    }

    asking.postMessage({ kind: 'ask' } satisfies Ask)
  })
}
