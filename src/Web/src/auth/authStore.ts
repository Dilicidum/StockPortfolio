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

/**
 * A tiny external store rather than plain React state, for one specific reason:
 * TanStack Router's `beforeLoad` is a plain synchronous function running outside
 * React. It cannot call a hook and it cannot await a render. The route guard
 * therefore needs a way to ask "is there a session right now?" that answers
 * immediately from outside the tree — `authStore.getState()`.
 *
 * React components read the same state through `useAuthState()`, which is
 * `useSyncExternalStore`, so the guard and the UI can never disagree.
 *
 * `useSyncExternalStore` requires a stable snapshot reference: returning a fresh
 * object on every call is an infinite re-render loop. Hence one frozen `state`
 * object, replaced only on a real change.
 *
 * The store holds identity, never tokens — those live in lib/tokenStore.ts.
 */
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

/** Call only once tokens are stored — the shell renders immediately after. */
function setUser(user: AuthUser): void {
  state = Object.freeze({ user, isAuthenticated: true })
  emit()
}

/** Logout, failed bootstrap, or an unrecoverable 401. Idempotent. */
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
