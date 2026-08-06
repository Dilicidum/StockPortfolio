import { createFileRoute, Outlet, redirect } from '@tanstack/react-router'
import { useAlertStream } from '../alerts/useAlertStream'
import { useSyncServerLanguage } from '../settings/useSyncServerLanguage'
import { useSyncServerTheme } from '../settings/useSyncServerTheme'

export const Route = createFileRoute('/_authenticated')({
  beforeLoad: ({ context, location }) => {
    if (!context.auth.getState().isAuthenticated) {
      throw redirect({ to: '/login', search: { redirect: location.href } })
    }
  },
  component: AuthenticatedLayout,
})

function AuthenticatedLayout() {
  useAlertStream()
  useSyncServerLanguage()
  useSyncServerTheme()

  return <Outlet />
}
