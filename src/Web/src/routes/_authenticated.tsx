import { createFileRoute, Outlet, redirect } from '@tanstack/react-router'

/**
 * THE GUARD.
 *
 * `beforeLoad` is synchronous and runs outside React, so it cannot wait for
 * anything. That is the whole reason main.tsx finishes the session bootstrap
 * before mounting the router: if the refresh call were kicked off from an
 * effect, this function would run first, see `isAuthenticated === false` on
 * every hard refresh of /dashboard, and bounce the user to /login — the P0
 * session-persistence requirement failing while every unit test passes.
 *
 * `location.href` is captured so the user lands back where they were aiming
 * after signing in. It is validated on the way out (see lib/safeRedirect.ts).
 *
 * Pathless layout route (`_authenticated`) rather than a path segment: the URL
 * stays /dashboard, not /_authenticated/dashboard, and every route filed under
 * this one inherits the guard by existing. There is no way to add a protected
 * page in phase 2 and forget to protect it.
 */
export const Route = createFileRoute('/_authenticated')({
  beforeLoad: ({ context, location }) => {
    if (!context.auth.getState().isAuthenticated) {
      throw redirect({ to: '/login', search: { redirect: location.href } })
    }
  },
  component: () => <Outlet />,
})
