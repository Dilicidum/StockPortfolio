import { createContext, type ReactNode } from 'react'
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
  isSigningOut: boolean
}

export const AuthContext = createContext<AuthContextValue | null>(null)

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
      authStore.signOut()
      queryClient.clear()
      await router.invalidate()
    },
  })

  const value: AuthContextValue = {
    user,
    isAuthenticated,
    login: (credentials) => signInMutation.mutateAsync(credentials),
    register: (credentials) => signUpMutation.mutateAsync(credentials),
    logout: async () => {
      await signOutMutation.mutateAsync()
    },
    isSigningOut: signOutMutation.isPending,
  }

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
