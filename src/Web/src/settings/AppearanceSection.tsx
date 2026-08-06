import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { SelectField } from '../components/SelectField'
import { applyTheme, cacheTheme, type ThemeChoice } from '../lib/theme'
import { appearanceKeys, appearanceQuery, type AppearanceSettings } from './appearanceApi'
import { saveAppearancePatch } from './settingsApi'
import { SettingCard } from './SettingCard'
import { useSavedSetting } from './useSavedSetting'

const THEMES: ThemeChoice[] = ['light', 'dark', 'system']

export function AppearanceSection() {
  const { t } = useTranslation('settings')
  const { data } = useQuery(appearanceQuery)

  const theme = useSavedSetting<ThemeChoice, AppearanceSettings>({
    serverValue: data?.theme as ThemeChoice | undefined,
    fallback: 'system',
    queryKey: appearanceKeys.view(),
    mutationFn: (choice) => saveAppearancePatch(data, { theme: choice }),
    onSaved: (choice) => {
      applyTheme(choice)
      cacheTheme(choice)
    },
  })

  return (
    <SettingCard title={t('appearance.title')} save={theme.save} onSave={theme.submit}>
      <SelectField
        label={t('appearance.themeLabel')}
        value={theme.value}
        onChange={(event) => theme.change(event.target.value as ThemeChoice)}
        options={THEMES.map((choice) => ({
          value: choice,
          label: t(`appearance.themeOptions.${choice}`),
        }))}
      />
    </SettingCard>
  )
}
