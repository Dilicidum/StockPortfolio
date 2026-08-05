import { useRef, useState } from 'react'
import { createFileRoute, useRouter, type ErrorComponentProps } from '@tanstack/react-router'
import { useSuspenseQuery } from '@tanstack/react-query'
import { Controller, useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Alert } from '../../components/Alert'
import { AppShell } from '../../components/AppShell'
import { Button } from '../../components/Button'
import { Card } from '../../components/Card'
import { ConfirmDialog } from '../../components/ConfirmDialog'
import { Table, type Column } from '../../components/Table'
import { TextField } from '../../components/TextField'
import { TickerCell } from '../../components/TickerCell'
import { TickerCombobox } from '../../marketdata/TickerCombobox'
import { formatMoney } from '../../lib/format'
import { applyServerErrors } from '../../lib/formErrors'
import { EditHoldingForm, type EditHoldingValues } from '../../portfolio/EditHoldingForm'
import { holdingsQuery, type Holding } from '../../portfolio/holdingsApi'
import { useAddHolding, useRemoveHolding, useUpdateHolding } from '../../portfolio/useHoldingMutations'

/**
 * THE FIRST LOADER IN THE APPLICATION, and deliberately so.
 *
 * Holdings are the page — there is nothing worth rendering without them, so the
 * router waits and the component below can use `useSuspenseQuery` and never see
 * an `undefined`. Phase 3's quotes are the opposite case and must NOT get a
 * loader: a slow provider would then hold the whole route hostage.
 *
 * `queryClient` comes from router context, never the module singleton, or the
 * memory routers the tests build would warm a different cache than the one the
 * component reads.
 */
export const Route = createFileRoute('/_authenticated/portfolio')({
  loader: ({ context: { queryClient } }) => queryClient.ensureQueryData(holdingsQuery),
  component: PortfolioPage,
  errorComponent: PortfolioError,
})

/**
 * ROUTE-LEVEL, not a router-wide `defaultErrorComponent`. A router-wide default also
 * covers /login and /register, which are not inside `AppShell` — so it could not render
 * the shell, which is the whole point of having one here.
 *
 * Without it, `useSuspenseQuery` (`throwOnError` defaults to true) hands any holdings
 * rejection to TanStack Router's built-in "Something went wrong!" panel: no shell, no
 * nav, no retry. The `invalidateQueries` in every mutation's `onSettled` reaches it
 * too, so a failed mutation could tear the page down a moment after the optimistic
 * rollback had repaired it.
 */
function PortfolioError({ error }: ErrorComponentProps) {
  const router = useRouter()

  return (
    <AppShell title="Portfolio" subtitle="Positions you hold, and what you paid for them">
      <Alert tone="error" title="Could not load your positions">
        {error.message || 'The server did not answer.'}
      </Alert>

      {/* `router.invalidate()` re-runs the loader, which re-runs `ensureQueryData`
          against a query that holds an error and no data — so it refetches. */}
      <div className="sm:max-w-[200px]">
        <Button onClick={() => void router.invalidate()}>Try again</Button>
      </div>
    </AppShell>
  )
}

/**
 * zod v4 spelling: `z.string()` / `z.coerce.number()` at the top level, not the v3
 * `z.string().email()` chain. Every message is a KEY rather than a sentence, so
 * phase 5's i18n can translate it without editing a validation rule; until then the
 * key itself is what shows, which is ugly and honest rather than pretty and wrong.
 *
 * The regex mirrors `Ticker`'s `^[A-Z]{1,5}$` on the server but accepts lower case,
 * because the server canonicalises. This copy saves a round trip; it is not the
 * authority, and where the two disagree the server's 400 lands under the field.
 */
const addHoldingSchema = z.object({
  ticker: z.string().regex(/^[A-Za-z]{1,5}$/, 'errors.ticker.format'),
  quantity: z.coerce.number().positive('errors.quantity.positive'),
  price: z.coerce.number().positive('errors.price.positive'),
})

/** What the inputs hold (strings off the DOM) versus what the schema hands the submit (numbers). */
type AddHoldingInput = z.input<typeof addHoldingSchema>
type AddHoldingValues = z.output<typeof addHoldingSchema>

/**
 * Invested is summed from the server's own per-row figures, never recomputed from
 * quantity x price. The server rounds the average to 6dp on store; multiplying a
 * rounded average in float64 here would disagree with it, and a totals row is exactly
 * where such a disagreement accumulates until someone notices it on a screenshot.
 *
 * `Number(...)` on a money string is a float, and that is acceptable ONLY because this
 * value is displayed and then thrown away. It must never be sent back or compared.
 *
 * DEFERRED, not overlooked — this is the `CLAUDE.md` "never compute money in the
 * browser" breach, carried one more phase. Phase 3's server-computed equivalent is
 * `GetDashboardResult.totals.cost`, and the only way to reach it is `/api/dashboard`,
 * which fans out one provider HTTP call per position against a 60-calls-per-minute
 * budget. Spending that budget from a page that shows no prices is a worse trade than
 * the breach. The real fix is a cost total on `GET /api/holdings`' own response, which
 * no phase has scheduled yet.
 */
function totalInvested(holdings: Holding[]): string {
  const total = holdings.reduce((sum, holding) => sum + Number(holding.invested.amount), 0)

  return total.toLocaleString(undefined, {
    style: 'currency',
    currency: holdings[0]?.invested.currency ?? 'USD',
  })
}

export function PortfolioPage() {
  const { data: holdings } = useSuspenseQuery(holdingsQuery)

  const add = useAddHolding()
  const update = useUpdateHolding()
  const remove = useRemoveHolding()

  const [formError, setFormError] = useState('')
  const [merged, setMerged] = useState<Holding | null>(null)
  const [removing, setRemoving] = useState<Holding | null>(null)
  const [editing, setEditing] = useState<Holding | null>(null)

  // The Edit button that opened the panel, so focus can go back to it on close.
  const editOpenerRef = useRef<HTMLElement | null>(null)

  const {
    register,
    control,
    handleSubmit,
    setError,
    reset,
    formState: { errors },
  } = useForm<AddHoldingInput, unknown, AddHoldingValues>({
    resolver: zodResolver(addHoldingSchema),
    defaultValues: { ticker: '', quantity: '', price: '' },
  })

  const onSubmit = handleSubmit(async (values) => {
    setFormError('')
    setMerged(null)

    try {
      const result = await add.mutateAsync(values)
      if (result.merged) setMerged(result.holding)
      reset()
    } catch (error) {
      // A 400's field errors land under their fields; anything else (409, 500)
      // becomes the banner, exactly as login.tsx does it.
      setFormError(applyServerErrors(error, setError, ['ticker', 'quantity', 'price']))
    }
  })

  function openEditor(holding: Holding) {
    setFormError('')
    editOpenerRef.current = document.activeElement as HTMLElement | null
    setEditing(holding)
  }

  function closeEditor() {
    setEditing(null)
    // Focus is inside a form that is about to unmount. Hand it back to the Edit button
    // that opened it, rather than letting the browser drop it on <body>.
    editOpenerRef.current?.focus()
  }

  /** Rejects on failure, which is how `EditHoldingForm` learns to place the server's errors. */
  async function saveCorrection(values: EditHoldingValues) {
    if (!editing) return

    setFormError('')
    setMerged(null)

    await update.mutateAsync({ id: editing.id, body: values })
    closeEditor()
  }

  const columns: Array<Column<Holding>> = [
    { header: 'Asset', cell: (holding) => <TickerCell ticker={holding.ticker} name={holding.name} /> },
    { header: 'Qty', cell: (holding) => holding.quantity, numeric: true },
    { header: 'Buy', cell: (holding) => formatMoney(holding.averagePrice), numeric: true },
    {
      header: 'Actions',
      // `numeric` only for its right alignment; `font-sans` puts the word "Edit" back
      // into the body face, because the monospace half of that flag is for figures.
      numeric: true,
      cell: (holding) => (
        <div className="flex items-center justify-end gap-1 font-sans">
          <Button
            variant="ghost"
            size="sm"
            // Same shape as Remove below: the accessible name names the row, because
            // "Edit" repeated once per position tells a screen-reader user nothing.
            aria-label={`Edit ${holding.ticker}`}
            onClick={() => openEditor(holding)}
          >
            Edit
          </Button>
          <Button
            variant="ghost"
            size="sm"
            // The visible label is a glyph; the accessible name says which row it acts on,
            // because "×" repeated once per position tells a screen-reader user nothing.
            aria-label={`Remove ${holding.ticker}`}
            onClick={() => setRemoving(holding)}
          >
            <span aria-hidden="true">×</span>
          </Button>
        </div>
      ),
    },
  ]

  return (
    <AppShell title="Portfolio" subtitle="Positions you hold, and what you paid for them">
      {formError ? <Alert tone="error">{formError}</Alert> : null}

      {/*
       * The phase's demo moment. Two buys of the same ticker collapse into one row at a
       * weighted average, and a silent row update would hide the only interesting
       * business rule in Phase 2. tone="success" renders role="status" (polite).
       */}
      {merged ? (
        <Alert tone="success">
          Merged into your {merged.ticker} position — {merged.quantity} shares, average{' '}
          {formatMoney(merged.averagePrice)}.
        </Alert>
      ) : null}

      <Card title="Add a position">
        <form onSubmit={onSubmit} noValidate className="flex flex-col gap-4">
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
            {/*
             * `Controller` rather than `register`, because the combobox has to be able to
             * WRITE the field — picking "Apple Inc" has to put AAPL in the box — and a
             * registered uncontrolled input can only be read. It also keeps a keystroke
             * from re-rendering the positions table, which `watch('ticker')` would.
             */}
            <Controller
              control={control}
              name="ticker"
              render={({ field, fieldState }) => (
                <TickerCombobox
                  label="Ticker"
                  placeholder="AAPL"
                  value={field.value ?? ''}
                  onChange={field.onChange}
                  onBlur={field.onBlur}
                  inputRef={field.ref}
                  error={fieldState.error?.message}
                />
              )}
            />
            <TextField
              label="Quantity"
              type="number"
              step="any"
              inputMode="decimal"
              placeholder="10"
              error={errors.quantity?.message}
              {...register('quantity')}
            />
            <TextField
              label="Price"
              type="number"
              step="any"
              inputMode="decimal"
              placeholder="100.00"
              error={errors.price?.message}
              {...register('price')}
            />
          </div>

          {/*
           * Disabled while pending. This is the whole client-side defence against the
           * merge race: two identical POSTs both pass the server's "do you already hold
           * this?" lookup, one wins the unique index and the other 500s, and a
           * double-click is the only realistic way to produce two of them.
           */}
          <div className="sm:max-w-[200px]">
            <Button type="submit" size="lg" disabled={add.isPending}>
              {add.isPending ? 'Adding…' : 'Add position'}
            </Button>
          </div>
        </form>
      </Card>

      <Card
        title="Positions"
        action={
          <span className="text-mu text-[12.5px]">
            Invested <span className="text-tx font-mono">{totalInvested(holdings)}</span>
          </span>
        }
      >
        {editing ? (
          <EditHoldingForm
            // Keyed on the row: react-hook-form reads `defaultValues` once, at mount.
            // Without the key, opening a second row while one is open would keep the
            // first row's numbers in the fields.
            key={editing.id}
            holding={editing}
            pending={update.isPending}
            onSave={saveCorrection}
            onError={setFormError}
            onCancel={closeEditor}
          />
        ) : null}

        {/* No price and no P&L columns on purpose: the dashboard owns them. Adding them here would
            make a CRUD screen pay MarketData's one-call-per-position fan-out on every render. */}
        <Table
          caption="Your positions"
          columns={columns}
          rows={holdings}
          rowKey={(holding) => holding.id}
          empty="No positions yet. Add one above."
        />
      </Card>

      <ConfirmDialog
        open={removing !== null}
        title="Remove position"
        body={
          removing
            ? `This removes your ${removing.ticker} position and everything recorded against it.`
            : ''
        }
        confirmLabel="Remove"
        onCancel={() => setRemoving(null)}
        // No `busy`: the removal is optimistic, so the row is already gone by the time
        // the request is in flight. Holding a spinner over a table that has already
        // updated would be a progress indicator for something the user can see finished.
        onConfirm={() => {
          const target = removing
          setRemoving(null)
          if (!target) return

          setFormError('')

          // `mutate` never throws, so a failed DELETE used to be consumed entirely by
          // the rollback: the row left, came back a second later, and nothing said why.
          // That reads as a rendering glitch and invites a second click at a server
          // that just failed. `mutateAsync` + catch is what the add path already does.
          void remove.mutateAsync(target.id).catch(() => {
            setFormError(`Could not remove ${target.ticker}. Please try again.`)
          })
        }}
      />
    </AppShell>
  )
}
