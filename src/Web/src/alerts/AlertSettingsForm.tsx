import { useId } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Button } from '../components/Button'
import { TextField } from '../components/TextField'
import { applyServerErrors } from '../lib/formErrors'
import type { AlertSetting } from './alertsApi'

/**
 * A threshold belongs to a POSITION, not to an account — one per user and ticker — so this
 * form is opened from a row on the portfolio page rather than sitting on a settings screen.
 *
 * NO COMPONENT LIBRARY. The window is a native `<select>` and the on/off is an
 * `<input type="checkbox" role="switch">`; the brief bans UI kits and both of those already
 * carry the keyboard behaviour, the focus ring and the accessible state that a hand-rolled
 * div would have to reimplement badly.
 *
 * The message keys match the two other forms, so phase 5's i18n has one place to translate
 * from rather than three.
 */
const alertSettingSchema = z.object({
  thresholdPercent: z.coerce
    .number()
    .positive('errors.threshold.positive')
    .max(100, 'errors.threshold.max'),
  windowMinutes: z.coerce.number().int().positive('errors.window.positive'),
  enabled: z.boolean(),
})

/** What the inputs hold (strings off the DOM) versus what the schema hands the submit. */
type AlertSettingInput = z.input<typeof alertSettingSchema>
export type AlertSettingValues = z.output<typeof alertSettingSchema>

/**
 * Capped at an hour, matching the server's `Alerts:MaxWindowMinutes`. "Moved sharply" is a
 * minutes-to-an-hour idea; a move over days is a trend, which is a different product. The
 * server rejects an over-cap window with a 409 naming both numbers, so this list is a
 * convenience rather than the rule — which is why the offered values are a plain array and
 * not derived from anything.
 */
const WINDOWS = [5, 15, 30, 60]

const DEFAULT_THRESHOLD = 5
const DEFAULT_WINDOW = 15

export interface AlertSettingsFormProps {
  ticker: string
  /** The threshold already stored for this position, if there is one. */
  setting?: AlertSetting | undefined
  pending: boolean
  /** Rejects with the API error: field errors land under their fields, the rest goes to `onError`. */
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
      // A new threshold arrives switched on: nobody opens this panel to create a
      // disabled rule, and the switch is right there to turn an existing one off.
      enabled: setting?.enabled ?? true,
    },
  })

  const onSubmit = handleSubmit(async (values) => {
    try {
      await onSave(values)
    } catch (error) {
      // Same split as every other form here: a 400's field errors go under their fields,
      // and the two 409s — you do not hold this ticker, that window is longer than the
      // server keeps history for — become the page banner.
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
          Alert on {ticker}
        </h3>
        <p className="text-mu text-[11.5px] leading-relaxed">
          Tell me when {ticker} moves by more than this much, either way, inside the window.
          Repeat alerts on the same move are held back for a few minutes.
        </p>
      </div>

      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <TextField
          label="Move of at least (%)"
          type="number"
          step="any"
          inputMode="decimal"
          autoFocus
          error={errors.thresholdPercent?.message}
          {...register('thresholdPercent')}
        />

        <div className="flex flex-col gap-[7px]">
          <label htmlFor={windowId} className="text-mu text-xs">
            Within
          </label>
          <select
            id={windowId}
            className="border-bd bg-panel text-tx rounded-[9px] border px-[13px] py-[11px] text-sm"
            {...register('windowMinutes')}
          >
            {WINDOWS.map((minutes) => (
              <option key={minutes} value={minutes}>
                {minutes} minutes
              </option>
            ))}
          </select>
          {errors.windowMinutes?.message ? (
            <span role="alert" className="text-dn text-[11.5px]">
              {errors.windowMinutes.message}
            </span>
          ) : null}
        </div>
      </div>

      {/*
       * `role="switch"` on a real checkbox: the input keeps space-to-toggle, the focus ring
       * and `aria-checked` for free, and the role is what makes a screen reader announce
       * "on"/"off" rather than "checked". A div with a click handler has none of that.
       */}
      <label htmlFor={switchId} className="text-mu flex items-center gap-2.5 text-[12.5px]">
        <input
          id={switchId}
          type="checkbox"
          role="switch"
          className="accent-ac h-4 w-4"
          {...register('enabled')}
        />
        Alerting on
      </label>

      <div className="flex gap-2">
        <Button type="submit" size="sm" disabled={pending}>
          {pending ? 'Saving…' : 'Save alert'}
        </Button>
        <Button type="button" variant="secondary" size="sm" onClick={onCancel}>
          Cancel
        </Button>
      </div>
    </form>
  )
}
