import { createFileRoute, redirect } from '@tanstack/react-router'

/**
 * "/" is a fork, never a page. The store is read synchronously here for the
 * same reason the guard does: by the time this runs, main.tsx has already
 * finished the session bootstrap, so the answer is final rather than "not yet".
 */
export const Route = createFileRoute('/')({
  beforeLoad: ({ context }) => {
    throw redirect({ to: context.auth.getState().isAuthenticated ? '/dashboard' : '/login' })
  },
})
