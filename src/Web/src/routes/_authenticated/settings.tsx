import { createFileRoute, useRouter, type ErrorComponentProps } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { Alert } from '../../components/Alert'
import { AppShell } from '../../components/AppShell'
import { Button } from '../../components/Button'
import { ApiKeySection } from '../../settings/ApiKeySection'
import { AppearanceSection } from '../../settings/AppearanceSection'
import { LanguageSection } from '../../settings/LanguageSection'
import { QuotesSection } from '../../settings/QuotesSection'
import { VisibilitySection } from '../../settings/VisibilitySection'

export const Route = createFileRoute('/_authenticated/settings')({
  component: SettingsPage,
  errorComponent: SettingsError,
})

function SettingsError({ error }: ErrorComponentProps) {
  const router = useRouter()
  const { t } = useTranslation(['settings', 'common'])

  return (
    <AppShell title={t('title')} subtitle={t('subtitle')}>
      <Alert tone="error" title={t('error.title')}>
        {error.message || t('error.fallback')}
      </Alert>

      <div className="sm:max-w-[200px]">
        <Button onClick={() => void router.invalidate()}>{t('common:actions.tryAgain')}</Button>
      </div>
    </AppShell>
  )
}

function SettingsPage() {
  const { t } = useTranslation('settings')

  return (
    <AppShell title={t('title')} subtitle={t('subtitle')}>
      <AppearanceSection />
      <LanguageSection />
      <QuotesSection />
      <ApiKeySection />
      <VisibilitySection />
    </AppShell>
  )
}
