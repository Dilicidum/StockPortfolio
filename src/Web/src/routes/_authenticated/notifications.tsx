import { createFileRoute } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { AlertPanel } from '../../alerts/AlertPanel'
import { AppShell } from '../../components/AppShell'

export const Route = createFileRoute('/_authenticated/notifications')({
  component: NotificationsPage,
})

function NotificationsPage() {
  const { t } = useTranslation('alerts')

  return (
    <AppShell title={t('notificationsPage.title')} subtitle={t('notificationsPage.subtitle')}>
      <AlertPanel />
    </AppShell>
  )
}
