import { createContext, useCallback, useMemo, type ReactNode } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useRouter } from '@tanstack/react-router'
import { authKeys, login, logout, register, type Credentials } from './authApi'
import { authStore, useAuthState, type AuthUser } from './authStore'

export interface AuthContextValue {
  user: AuthUser | null
  isAuthenticated: boolean
  login: (credentials: Credentials) => Promise<AuthUser>
  register: (credentials: Credentials) => Promise<AuthUser>
  logout: () => Promise<void>
  isSigningIn: boolean
  isSigningOut: boolean
}

export const AuthContext = createContext<AuthContextValue | null>(null)

/**
 * Every network call goes through TanStack Query — login/register/logout as
 * mutations, /me as a query seeded by the bootstrap in main.tsx.
 *
 * After any auth transition we call `router.invalidate()`. Route guards run in
 * `beforeLoad`, which the router will not re-run on its own just because some
 * React state changed; without the invalidate, logging out leaves you sitting
 * on `/dashboard` until the next navigation.
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const { user, isAuthenticated } = useAuthState()
  const queryClient = useQueryClient()
  const router = useRouter()

  const signInMutation = useMutation({
    mutationFn: (credentials: Credentials) => login(credentials),
    onSuccess: async (signedIn) => {
      queryClient.setQueryData(authKeys.me, signedIn)
      await router.invalidate()
    },
  })

  const signUpMutation = useMutation({
    mutationFn: (credentials: Credentials) => register(credentials),
    onSuccess: async (signedIn) => {
      queryClient.setQueryData(authKeys.me, signedIn)
      await router.invalidate()
    },
  })

  const signOutMutation = useMutation({
    mutationFn: () => logout(),
    onSettled: async () => {
      // onSettled, not onSuccess: authApi.logout drops the local session even
      // when the server call fails, so the cache must be cleared either way or
      // the next user of this tab inherits the previous one's cached data.
      authStore.signOut()
      queryClient.clear()
      await router.invalidate()
    },
  })

  const doLogin = useCallback(
    (credentials: Credentials) => signInMutation.mutateAsync(credentials),
    [signInMutation],
  )
  const doRegister = useCallback(
    (credentials: Credentials) => signUpMutation.mutateAsync(credentials),
    [signUpMutation],
  )
  const doLogout = useCallback(async () => {
    await signOutMutation.mutateAsync()
  }, [signOutMutation])

  const value = useMemo<AuthContextValue>(
    () => ({
      user,
      isAuthenticated,
      login: doLogin,
      register: doRegister,
      logout: doLogout,
      isSigningIn: signInMutation.isPending || signUpMutation.isPending,
      isSigningOut: signOutMutation.isPending,
    }),
    [
      user,
      isAuthenticated,
      doLogin,
      doRegister,
      doLogout,
      signInMutation.isPending,
      signUpMutation.isPending,
      signOutMutation.isPending,
    ],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
