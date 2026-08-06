import { queryClient } from '../lib/queryClient'
import { REFRESH_TOKEN_STORAGE_KEY, getRefreshToken } from '../lib/tokenStore'
import { authKeys, restoreSession } from './authApi'
import { authStore } from './authStore'

/**
 * CROSS-TAB SESSION, using the browser's own mechanism.
 *
 * The refresh token is in localStorage, so every tab already shares it. All
 * that is left is telling the other tabs when it changes, and the `storage`
 * event does exactly that: it fires in every tab EXCEPT the one that made the
 * change, which is precisely the audience that needs to know.
 *
 * Three cases, and the third is the one worth reading twice:
 *
 *   gone      signed out elsewhere -> sign out here, now.
 *   appeared  signed in elsewhere  -> restore the session here.
 *   changed   a rotation elsewhere -> do nothing. This tab reads the token
 *             from storage at the moment it refreshes, so it will pick up the
 *             new value on its own. Reacting would mean two tabs racing to
 *             spend a single-use token, which is the bug the old message bus
 *             had to keep broadcasting rotations to avoid.
 *
 * This replaces auth/sessionChannel.ts, which used a BroadcastChannel to let a
 * tab with no credential ask the others for one and adopt whatever came back.
 * That signed a brand-new tab in silently, and it left sign-out stranded in
 * the tab it happened in. Nothing here can sign a tab in that the browser was
 * not already holding a credential for.
 */
export function startSessionSync(): () => void {
  const onStorage = (event: StorageEvent): void => {
    // `key === null` means localStorage.clear(); anything else that is not our
    // key belongs to someone else. `storageArea` guards against sessionStorage
    // events, which share this listener and would otherwise look identical.
    if (event.storageArea && event.storageArea !== globalThis.localStorage) return
    if (event.key !== null && event.key !== REFRESH_TOKEN_STORAGE_KEY) return

    const token = getRefreshToken()
    const { isAuthenticated } = authStore.getState()

    if (!token) {
      // Idempotent, and it does not write back to storage in a way that could
      // echo: removing an already-absent key fires no event anywhere.
      authStore.signOut()
      queryClient.clear()
      return
    }

    if (!isAuthenticated) {
      // Signed in in another tab. Failure is the ordinary "that token was no
      // good after all" path and leaves this tab exactly where it was.
      //
      // staleTime: 0 is load-bearing and not a default worth inheriting. The
      // query client sets 30s globally, so without this a cached identity from
      // earlier in this tab's life would be handed back and the new token would
      // never be exchanged — leaving the tab "signed in" with no access token.
      void queryClient
        .fetchQuery({
          queryKey: authKeys.me,
          queryFn: () => restoreSession(),
          retry: false,
          staleTime: 0,
        })
        .catch(() => authStore.signOut())
    }
  }

  globalThis.addEventListener?.('storage', onStorage)

  return () => globalThis.removeEventListener?.('storage', onStorage)
}
