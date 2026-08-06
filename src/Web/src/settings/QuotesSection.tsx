import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { SelectField } from '../components/SelectField'
import {
  DEFAULT_REFRESH_SECONDS,
  REFRESH_INTERVAL_SECONDS,
  dashboardSettingsQuery,
  saveDashboardSettings,
  settingsKeys,
  type DashboardSettings,
} from './settingsApi'
import { SettingCard } from './SettingCard'
import { useSavedSetting } from './useSavedSetting'

export function QuotesSection() {
  const { t } = useTranslation('settings')
  const { data } = useQuery(dashboardSettingsQuery)

  const interval = useSavedSetting<number, DashboardSettings>({
    serverValue: data?.refreshIntervalSeconds,
    fallback: DEFAULT_REFRESH_SECONDS,
    queryKey: settingsKeys.dashboard(),
    mutationFn: (seconds) => saveDashboardSettings({ refreshIntervalSeconds: seconds }),
  })

  return (
    <SettingCard title={t('quotes.title')} save={interval.save} onSave={interval.submit}>
      <SelectField
        label={t('quotes.intervalLabel')}
        value={interval.value}
        onChange={(event) => interval.change(Number(event.target.value))}
        options={REFRESH_INTERVAL_SECONDS.map((seconds) => ({
          value: seconds,
          label: t(`quotes.intervalOptions.${seconds}`),
        }))}
      />
      <p className="text-mu text-[11.5px] leading-relaxed">{t('quotes.costNote')}</p>
    </SettingCard>
  )
}
