import { createFileRoute } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { AppShell } from '../../components/AppShell'
import { ApiKeySection } from '../../settings/ApiKeySection'
import { AppearanceSection } from '../../settings/AppearanceSection'
import { LanguageSection } from '../../settings/LanguageSection'
import { QuotesSection } from '../../settings/QuotesSection'
import { VisibilitySection } from '../../settings/VisibilitySection'

export const Route = createFileRoute('/_authenticated/settings')({
  component: SettingsPage,
})

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
