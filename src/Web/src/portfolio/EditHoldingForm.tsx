import { useId } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { useTranslation } from 'react-i18next'
import { z } from 'zod'
import { Button } from '../components/Button'
import { TextField } from '../components/TextField'
import { applyServerErrors, translateFieldError } from '../lib/formErrors'
import type { Holding } from './holdingsApi'

const editHoldingSchema = z.object({
  quantity: z.coerce.number().positive('errors.quantity.positive'),
  price: z.coerce.number().positive('errors.price.positive'),
})

type EditHoldingInput = z.input<typeof editHoldingSchema>
export type EditHoldingValues = z.output<typeof editHoldingSchema>

export interface EditHoldingFormProps {
  holding: Holding
  pending: boolean
  onSave: (values: EditHoldingValues) => Promise<void>
  onError: (message: string) => void
  onCancel: () => void
}

export function EditHoldingForm({ holding, pending, onSave, onError, onCancel }: EditHoldingFormProps) {
  const headingId = useId()
  const { t } = useTranslation(['portfolio', 'common'])

  const {
    register,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<EditHoldingInput, unknown, EditHoldingValues>({
    resolver: zodResolver(editHoldingSchema),
    defaultValues: { quantity: String(holding.quantity), price: holding.averagePrice.amount },
  })

  const onSubmit = handleSubmit(async (values) => {
    try {
      await onSave(values)
    } catch (error) {
      onError(applyServerErrors(error, setError, ['quantity', 'price']))
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
          {t('editForm.heading', { ticker: holding.ticker })}
        </h3>
        <p className="text-mu text-[11.5px] leading-relaxed">{t('editForm.description')}</p>
      </div>

      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <TextField
          label={t('fields.quantityLabel')}
          type="number"
          step="any"
          inputMode="decimal"
          autoFocus
          error={translateFieldError(t, errors.quantity?.message)}
          {...register('quantity')}
        />
        <TextField
          label={t('fields.priceLabel')}
          type="number"
          step="any"
          inputMode="decimal"
          error={translateFieldError(t, errors.price?.message)}
          {...register('price')}
        />
      </div>

      <div className="flex gap-2">
        <Button type="submit" size="sm" disabled={pending}>
          {pending ? t('common:actions.saving') : t('common:actions.save')}
        </Button>
        <Button type="button" variant="secondary" size="sm" onClick={onCancel}>
          {t('common:actions.cancel')}
        </Button>
      </div>
    </form>
  )
}
