import { useSyncExternalStore } from 'react'
import { clearTokens } from '../lib/tokenStore'

export interface AuthUser {
  id: string
  email: string
}

export interface AuthState {
  user: AuthUser | null
  isAuthenticated: boolean
}

const UNAUTHENTICATED: AuthState = Object.freeze({ user: null, isAuthenticated: false })

let state: AuthState = UNAUTHENTICATED
const listeners = new Set<() => void>()

function emit(): void {
  for (const listener of listeners) listener()
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}

function getState(): AuthState {
  return state
}

function setUser(user: AuthUser): void {
  state = Object.freeze({ user, isAuthenticated: true })
  emit()
}

function signOut(): void {
  clearTokens()
  if (state === UNAUTHENTICATED) return
  state = UNAUTHENTICATED
  emit()
}

export const authStore = { subscribe, getState, setUser, signOut }

export function useAuthState(): AuthState {
  return useSyncExternalStore(subscribe, getState, getState)
}
