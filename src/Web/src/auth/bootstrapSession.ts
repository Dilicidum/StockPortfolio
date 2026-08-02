import { queryClient } from '../lib/queryClient'
import { authKeys, restoreSession } from './authApi'
import { authStore } from './authStore'

/**
 * Restores the session, if there is one, and never rejects.
 *
 * This lives in its own module rather than inline in main.tsx so a test can run
 * the *same* function the app runs. The P0 session-persistence criterion is
 * "hard refresh a guarded route and stay on it", and the way that requirement
 * fails is an ordering mistake in exactly these few lines — so the thing under
 * test has to be the shipping code, not a re-implementation of it.
 *
 * Awaiting this before mounting <RouterProvider> is what makes the synchronous
 * `beforeLoad` guard in routes/_authenticated.tsx correct. See main.tsx.
 */
export async function bootstrapSession(): Promise<void> {
  try {
    await queryClient.fetchQuery({
      queryKey: authKeys.me,
      queryFn: () => restoreSession(),
      retry: false,
      staleTime: 30_000,
    })
  } catch {
    // No refresh token, an expired one, or a rejected one — all of which mean
    // "not signed in". That is the normal state for a first-time visitor, not
    // an error, and there is nothing to tell the user about it.
    authStore.signOut()
  }
}
