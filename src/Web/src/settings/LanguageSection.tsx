import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { SelectField } from '../components/SelectField'
import { applyServerLanguage, SUPPORTED_LANGUAGES, type Language } from '../lib/i18n'
import { appearanceKeys, appearanceQuery, type AppearanceSettings } from './appearanceApi'
import { saveAppearancePatch } from './settingsApi'
import { SettingCard } from './SettingCard'
import { useSavedSetting } from './useSavedSetting'

export function LanguageSection() {
  const { t } = useTranslation('settings')
  const { data } = useQuery(appearanceQuery)

  const language = useSavedSetting<Language, AppearanceSettings>({
    serverValue: data?.language as Language | undefined,
    fallback: 'en',
    queryKey: appearanceKeys.view(),
    mutationFn: (choice) => saveAppearancePatch(data, { language: choice }),
    onSaved: (choice) => void applyServerLanguage(choice),
  })

  return (
    <SettingCard title={t('language.title')} save={language.save} onSave={language.submit}>
      <SelectField
        label={t('language.label')}
        value={language.value}
        onChange={(event) => language.change(event.target.value as Language)}
        options={SUPPORTED_LANGUAGES.map((choice) => ({
          value: choice,
          label: t(`language.options.${choice}`),
        }))}
      />
    </SettingCard>
  )
}
