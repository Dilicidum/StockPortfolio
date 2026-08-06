import { createFileRoute } from '@tanstack/react-router'
import { AlertPanel } from '../../alerts/AlertPanel'
import { AppShell } from '../../components/AppShell'

/**
 * The same rows as the dashboard panel, off the same query key, with nothing sliced off the
 * end. One key for both views is deliberate: the stream prepends into exactly one cache, so
 * a pushed alert cannot appear here and be missing from the panel.
 *
 * NO LOADER, for the dashboard's reason rather than the portfolio's. Alerts are history —
 * worth showing, never worth holding a route hostage for — so a failed fetch leaves the
 * page standing with a line saying so.
 */
export const Route = createFileRoute('/_authenticated/notifications')({
  component: NotificationsPage,
})

function NotificationsPage() {
  return (
    <AppShell
      title="Notifications"
      subtitle="Thresholds that have been crossed recently, newest first"
    >
      <AlertPanel />
    </AppShell>
  )
}
