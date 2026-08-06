import { queryClient } from '../lib/queryClient'
import { authKeys, restoreSession } from './authApi'
import { authStore } from './authStore'
import { startSessionSync } from './sessionSync'

export async function bootstrapSession(): Promise<void> {
  startSessionSync()

  try {
    await queryClient.fetchQuery({
      queryKey: authKeys.me,
      queryFn: () => restoreSession(),
      retry: false,
      staleTime: 30_000,
    })
  } catch {
    authStore.signOut()
  }
}
