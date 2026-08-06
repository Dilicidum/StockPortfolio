import { createFileRoute } from '@tanstack/react-router'
import { useTranslation } from 'react-i18next'
import { AppShell } from '../../components/AppShell'
import { ApiKeySection } from '../../settings/ApiKeySection'
import { AppearanceSection } from '../../settings/AppearanceSection'
import { LanguageSection } from '../../settings/LanguageSection'
import { QuotesSection } from '../../settings/QuotesSection'
import { VisibilitySection } from '../../settings/VisibilitySection'

/**
 * NO LOADER. Five independent `GET`s (D6: no aggregate `/api/settings` — each module serves
 * its own section) belong to the five sections themselves, exactly as the dashboard's own
 * `useQuery` calls do not hold the route hostage. A slow or failing section degrades on its
 * own, in its own `Card`, rather than blanking the whole screen.
 */
export const Route = createFileRoute('/_authenticated/settings')({
  component: SettingsPage,
})

function SettingsPage() {
  const { t } = useTranslation('settings')

  return (
    <AppShell title={t('title')} subtitle={t('subtitle')}>
      {/* Plan order: appearance, language, quotes, the user's own key, then visibility. */}
      <AppearanceSection />
      <LanguageSection />
      <QuotesSection />
      <ApiKeySection />
      <VisibilitySection />
    </AppShell>
  )
}
