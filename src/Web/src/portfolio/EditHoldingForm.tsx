import { useId } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Button } from '../components/Button'
import { TextField } from '../components/TextField'
import { applyServerErrors } from '../lib/formErrors'
import type { Holding } from './holdingsApi'

/**
 * Quantity and price only. `PATCH /api/holdings/{id}` is keyed on the holding id and
 * `UpdateHoldingBody` carries no ticker, so the asset itself is not correctable here —
 * a wrong ticker is a remove plus an add, not an edit.
 *
 * Message KEYS rather than sentences, matching the add form, so phase 5's i18n
 * translates both from one place instead of one of them being a hardcoded string.
 */
const editHoldingSchema = z.object({
  quantity: z.coerce.number().positive('errors.quantity.positive'),
  price: z.coerce.number().positive('errors.price.positive'),
})

/** What the inputs hold (strings off the DOM) versus what the schema hands the submit (numbers). */
type EditHoldingInput = z.input<typeof editHoldingSchema>
export type EditHoldingValues = z.output<typeof editHoldingSchema>

export interface EditHoldingFormProps {
  holding: Holding
  pending: boolean
  /** Rejects with the API error: field errors land under their fields, the rest goes to `onError`. */
  onSave: (values: EditHoldingValues) => Promise<void>
  onError: (message: string) => void
  onCancel: () => void
}

/**
 * An inline panel rather than a modal, deliberately. `ConfirmDialog`'s focus trap is
 * the only one in the app and its effects are ordered precisely; a second form-shaped
 * caller would mean either duplicating that trap or reworking a component whose
 * behaviour the delete test depends on. A form sitting in the page needs no trap at
 * all — it is already keyboard-reachable, and `autoFocus` moves focus into it.
 */
export function EditHoldingForm({ holding, pending, onSave, onError, onCancel }: EditHoldingFormProps) {
  const headingId = useId()

  const {
    register,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<EditHoldingInput, unknown, EditHoldingValues>({
    resolver: zodResolver(editHoldingSchema),
    // Prefilled from the row exactly as the server holds it — the quantity it counts
    // and the average-price STRING it stored. Nothing is reparsed or recomputed, so an
    // untouched field submits back the same value the server sent.
    defaultValues: { quantity: String(holding.quantity), price: holding.averagePrice.amount },
  })

  const onSubmit = handleSubmit(async (values) => {
    try {
      await onSave(values)
    } catch (error) {
      // Same split as login.tsx and the add form: a 400's field errors go under their
      // fields, anything else (404, 500) becomes the page banner.
      onError(applyServerErrors(error, setError, ['quantity', 'price']))
    }
  })

  return (
    <form
      onSubmit={onSubmit}
      noValidate
      // The accessible name is what makes this form addressable at all: it sits a few
      // rows above "Add a position" and carries the same two field labels.
      aria-labelledby={headingId}
      className="border-bd bg-panel-2 mb-4 flex flex-col gap-3 rounded-lg border p-3.5"
    >
      <div className="flex flex-col gap-1">
        <h3 id={headingId} className="text-tx text-[13px] font-semibold">
          Correct {holding.ticker}
        </h3>
        {/* Says REPLACES out loud. The add form averages; this one does not, and the two
            sit on the same screen, so the difference has to be stated rather than inferred. */}
        <p className="text-mu text-[11.5px] leading-relaxed">
          These values replace the position outright — nothing is averaged with what is
          recorded now. To add to it instead, buy it again above.
        </p>
      </div>

      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <TextField
          label="Quantity"
          type="number"
          step="any"
          inputMode="decimal"
          autoFocus
          error={errors.quantity?.message}
          {...register('quantity')}
        />
        <TextField
          label="Price"
          type="number"
          step="any"
          inputMode="decimal"
          error={errors.price?.message}
          {...register('price')}
        />
      </div>

      {/* Disabled while pending, like the add form: the PATCH is optimistic, so a second
          click would fire against a row the table already shows as corrected. */}
      <div className="flex gap-2">
        <Button type="submit" size="sm" disabled={pending}>
          {pending ? 'Saving…' : 'Save'}
        </Button>
        <Button type="button" variant="secondary" size="sm" onClick={onCancel}>
          Cancel
        </Button>
      </div>
    </form>
  )
}
