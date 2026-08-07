import { useId } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { useTranslation } from 'react-i18next'
import { z } from 'zod'
import { Button } from '../components/Button'
import { TextField } from '../components/TextField'
import { applyServerErrors, translateFieldError } from '../lib/formErrors'
import type { AlertSetting } from './alertsApi'

const alertSettingSchema = z.object({
  thresholdPercent: z.coerce
    .number()
    .positive('errors.threshold.positive')
    .max(100, 'errors.threshold.max'),
  windowMinutes: z.coerce.number().int().positive('errors.window.positive'),
  enabled: z.boolean(),
})

type AlertSettingInput = z.input<typeof alertSettingSchema>
export type AlertSettingValues = z.output<typeof alertSettingSchema>

const WINDOWS = [5, 15, 30, 60]

const DEFAULT_THRESHOLD = 5
const DEFAULT_WINDOW = 15

export interface AlertSettingsFormProps {
  ticker: string
  setting?: AlertSetting | undefined
  pending: boolean
  onSave: (values: AlertSettingValues) => Promise<void>
  onError: (message: string) => void
  onCancel: () => void
}

export function AlertSettingsForm({
  ticker,
  setting,
  pending,
  onSave,
  onError,
  onCancel,
}: AlertSettingsFormProps) {
  const { t } = useTranslation(['alerts', 'common'])
  const headingId = useId()
  const windowId = useId()
  const switchId = useId()

  const {
    register,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<AlertSettingInput, unknown, AlertSettingValues>({
    resolver: zodResolver(alertSettingSchema),
    defaultValues: {
      thresholdPercent: String(setting?.thresholdPercent ?? DEFAULT_THRESHOLD),
      windowMinutes: String(setting?.windowMinutes ?? DEFAULT_WINDOW),
      enabled: setting?.enabled ?? true,
    },
  })

  const onSubmit = handleSubmit(async (values) => {
    try {
      await onSave(values)
    } catch (error) {
      onError(applyServerErrors(error, setError, ['thresholdPercent', 'windowMinutes']))
    }
  })

  return (
    <form
      onSubmit={onSubmit}
      noValidate
      aria-labelledby={headingId}
      className="border-bd bg-panel-2 mb-4 flex flex-col gap-3 rounded-lg border p-3.5"
    >
      <div className="flex flex-col gap-1">
        <h3 id={headingId} className="text-tx text-[13px] font-semibold">
          {t('settingsForm.heading', { ticker })}
        </h3>
        <p className="text-mu text-[11.5px] leading-relaxed">
          {t('settingsForm.description', { ticker })}
        </p>
      </div>

      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <TextField
          label={t('settingsForm.thresholdLabel')}
          type="number"
          step="any"
          inputMode="decimal"
          autoFocus
          error={translateFieldError(t, errors.thresholdPercent?.message)}
          {...register('thresholdPercent')}
        />

        <div className="flex flex-col gap-[7px]">
          <label htmlFor={windowId} className="text-mu text-xs">
            {t('settingsForm.windowLabel')}
          </label>
          <select
            id={windowId}
            className="border-bd bg-panel text-tx rounded-[9px] border px-[13px] py-[11px] text-sm"
            {...register('windowMinutes')}
          >
            {WINDOWS.map((minutes) => (
              <option key={minutes} value={minutes}>
                {t('settingsForm.windowOption', { minutes })}
              </option>
            ))}
          </select>
          {errors.windowMinutes?.message ? (
            <span role="alert" className="text-dn text-[11.5px]">
              {translateFieldError(t, errors.windowMinutes.message)}
            </span>
          ) : null}
        </div>
      </div>

      <label htmlFor={switchId} className="text-mu flex items-center gap-2.5 text-[12.5px]">
        <input
          id={switchId}
          type="checkbox"
          role="switch"
          className="accent-ac h-4 w-4"
          {...register('enabled')}
        />
        {t('settingsForm.enabledLabel')}
      </label>

      <div className="flex gap-2">
        <Button type="submit" size="sm" disabled={pending}>
          {pending ? t('common:actions.saving') : t('settingsForm.submit')}
        </Button>
        <Button type="button" variant="secondary" size="sm" onClick={onCancel}>
          {t('common:actions.cancel')}
        </Button>
      </div>
    </form>
  )
}
