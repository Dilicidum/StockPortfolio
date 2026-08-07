import { queryClient } from '../lib/queryClient'
import { REFRESH_TOKEN_STORAGE_KEY, getRefreshToken } from '../lib/tokenStore'
import { authKeys, restoreSession } from './authApi'
import { authStore } from './authStore'

export function startSessionSync(): () => void {
  const onStorage = (event: StorageEvent): void => {
    if (event.storageArea && event.storageArea !== globalThis.localStorage) return
    if (event.key !== null && event.key !== REFRESH_TOKEN_STORAGE_KEY) return

    const token = getRefreshToken()
    const { isAuthenticated } = authStore.getState()

    if (!token) {
      authStore.signOut()
      queryClient.clear()
      return
    }

    if (!isAuthenticated) {
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
