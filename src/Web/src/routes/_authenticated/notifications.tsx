import { createFileRoute, useRouter, type ErrorComponentProps } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { AlertPanel } from '../../alerts/AlertPanel'
import { Alert } from '../../components/Alert'
import { AppShell } from '../../components/AppShell'
import { Button } from '../../components/Button'

export const Route = createFileRoute('/_authenticated/notifications')({
  component: NotificationsPage,
  errorComponent: NotificationsError,
})

function NotificationsError({ error }: ErrorComponentProps) {
  const router = useRouter()
  const { t } = useTranslation(['alerts', 'common'])

  return (
    <AppShell title={t('notificationsPage.title')} subtitle={t('notificationsPage.subtitle')}>
      <Alert tone="error" title={t('error.title')}>
        {error.message || t('error.fallback')}
      </Alert>

      <div className="sm:max-w-[200px]">
        <Button onClick={() => void router.invalidate()}>{t('common:actions.tryAgain')}</Button>
      </div>
    </AppShell>
  )
}

function NotificationsPage() {
  const { t } = useTranslation('alerts')

  return (
    <AppShell title={t('notificationsPage.title')} subtitle={t('notificationsPage.subtitle')}>
      <AlertPanel />
    </AppShell>
  )
}
