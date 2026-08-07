import { useRef, useState } from 'react'
import { createFileRoute, useRouter, type ErrorComponentProps } from '@tanstack/react-router'
import { useMutation, useQuery, useQueryClient, useSuspenseQuery } from '@tanstack/react-query'
import { Controller, useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { useTranslation } from 'react-i18next'
import { z } from 'zod'
import { AlertSettingsForm, type AlertSettingValues } from '../../alerts/AlertSettingsForm'
import { alertKeys, alertSettingsQuery, saveAlertSetting } from '../../alerts/alertsApi'
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
import { applyServerErrors, translateFieldError } from '../../lib/formErrors'
import i18n from '../../lib/i18n'
import { EditHoldingForm, type EditHoldingValues } from '../../portfolio/EditHoldingForm'
import { holdingsQuery, type Holding } from '../../portfolio/holdingsApi'
import { useAddHolding, useRemoveHolding, useUpdateHolding } from '../../portfolio/useHoldingMutations'

export const Route = createFileRoute('/_authenticated/portfolio')({
  loader: ({ context: { queryClient } }) => queryClient.ensureQueryData(holdingsQuery),
  component: PortfolioPage,
  errorComponent: PortfolioError,
})

function PortfolioError({ error }: ErrorComponentProps) {
  const router = useRouter()
  const { t } = useTranslation(['portfolio', 'common'])

  return (
    <AppShell title={t('title')} subtitle={t('subtitle')}>
      <Alert tone="error" title={t('error.title')}>
        {error.message || t('error.fallback')}
      </Alert>

      <div className="sm:max-w-[200px]">
        <Button onClick={() => void router.invalidate()}>{t('common:actions.tryAgain')}</Button>
      </div>
    </AppShell>
  )
}

const addHoldingSchema = z.object({
  ticker: z.string().regex(/^[A-Za-z]{1,5}$/, 'errors.ticker.format'),
  quantity: z.coerce.number().positive('errors.quantity.positive'),
  price: z.coerce.number().positive('errors.price.positive'),
})

type AddHoldingInput = z.input<typeof addHoldingSchema>
type AddHoldingValues = z.output<typeof addHoldingSchema>

function totalInvested(holdings: Holding[]): string {
  const total = holdings.reduce((sum, holding) => sum + Number(holding.invested.amount), 0)

  return total.toLocaleString(i18n.language, {
    style: 'currency',
    currency: holdings[0]?.invested.currency ?? 'USD',
  })
}

export function PortfolioPage() {
  const { t } = useTranslation(['portfolio', 'common'])
  const { data: holdings } = useSuspenseQuery(holdingsQuery)
  const queryClient = useQueryClient()

  const add = useAddHolding()
  const update = useUpdateHolding()
  const remove = useRemoveHolding()

  const { data: alertSettings } = useQuery(alertSettingsQuery)

  const saveAlert = useMutation({
    mutationFn: saveAlertSetting,
    onSettled: () => queryClient.invalidateQueries({ queryKey: alertKeys.settings() }),
  })

  const [formError, setFormError] = useState('')
  const [merged, setMerged] = useState<Holding | null>(null)
  const [removing, setRemoving] = useState<Holding | null>(null)
  const [editing, setEditing] = useState<Holding | null>(null)
  const [alerting, setAlerting] = useState<Holding | null>(null)

  const editOpenerRef = useRef<HTMLElement | null>(null)

  const settingFor = (ticker: string) =>
    alertSettings?.find((setting) => setting.ticker === ticker)

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
      setFormError(applyServerErrors(error, setError, ['ticker', 'quantity', 'price']))
    }
  })

  function openEditor(holding: Holding) {
    setFormError('')
    editOpenerRef.current = document.activeElement as HTMLElement | null
    setAlerting(null)
    setEditing(holding)
  }

  function closeEditor() {
    setEditing(null)
    editOpenerRef.current?.focus()
  }

  function openAlerts(holding: Holding) {
    setFormError('')
    editOpenerRef.current = document.activeElement as HTMLElement | null
    setEditing(null)
    setAlerting(holding)
  }

  function closeAlerts() {
    setAlerting(null)
    editOpenerRef.current?.focus()
  }

  async function saveThreshold(values: AlertSettingValues) {
    if (!alerting) return

    setFormError('')
    await saveAlert.mutateAsync({ ticker: alerting.ticker, ...values })
    closeAlerts()
  }

  async function saveCorrection(values: EditHoldingValues) {
    if (!editing) return

    setFormError('')
    setMerged(null)

    await update.mutateAsync({ id: editing.id, body: values })
    closeEditor()
  }

  const columns: Array<Column<Holding>> = [
    {
      header: t('positions.columns.asset'),
      cell: (holding) => <TickerCell ticker={holding.ticker} name={holding.name} />,
    },
    { header: t('positions.columns.qty'), cell: (holding) => holding.quantity, numeric: true },
    {
      header: t('positions.columns.buy'),
      cell: (holding) => formatMoney(holding.averagePrice),
      numeric: true,
    },
    {
      header: t('positions.columns.actions'),
      numeric: true,
      cell: (holding) => {
        const setting = settingFor(holding.ticker)

        return (
        <div className="flex items-center justify-end gap-1 font-sans">
          <Button
            variant="ghost"
            size="sm"
            aria-label={t('rowActions.setAlertAria', { ticker: holding.ticker })}
            onClick={() => openAlerts(holding)}
          >
            {setting && setting.enabled ? (
              <span className="text-ac font-mono">
                {t('rowActions.thresholdLabel', {
                  threshold: setting.thresholdPercent,
                  window: setting.windowMinutes,
                })}
              </span>
            ) : (
              t('rowActions.setAlert')
            )}
          </Button>
          <Button
            variant="ghost"
            size="sm"
            aria-label={t('rowActions.editAria', { ticker: holding.ticker })}
            onClick={() => openEditor(holding)}
          >
            {t('rowActions.edit')}
          </Button>
          <Button
            variant="ghost"
            size="sm"
            aria-label={t('rowActions.removeAria', { ticker: holding.ticker })}
            onClick={() => setRemoving(holding)}
          >
            <span aria-hidden="true">×</span>
          </Button>
        </div>
        )
      },
    },
  ]

  return (
    <AppShell title={t('title')} subtitle={t('subtitle')}>
      {formError ? <Alert tone="error">{formError}</Alert> : null}

      {merged ? (
        <Alert tone="success">
          {t('mergedNotice', {
            ticker: merged.ticker,
            quantity: merged.quantity,
            price: formatMoney(merged.averagePrice),
          })}
        </Alert>
      ) : null}

      <Card title={t('addForm.title')}>
        <form onSubmit={onSubmit} noValidate className="flex flex-col gap-4">
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
            <Controller
              control={control}
              name="ticker"
              render={({ field, fieldState }) => (
                <TickerCombobox
                  label={t('fields.tickerLabel')}
                  placeholder={t('fields.tickerPlaceholder')}
                  value={field.value ?? ''}
                  onChange={field.onChange}
                  onBlur={field.onBlur}
                  inputRef={field.ref}
                  error={translateFieldError(t, fieldState.error?.message)}
                />
              )}
            />
            <TextField
              label={t('fields.quantityLabel')}
              type="number"
              step="any"
              inputMode="decimal"
              placeholder={t('fields.quantityPlaceholder')}
              error={translateFieldError(t, errors.quantity?.message)}
              {...register('quantity')}
            />
            <TextField
              label={t('fields.priceLabel')}
              type="number"
              step="any"
              inputMode="decimal"
              placeholder={t('fields.pricePlaceholder')}
              error={translateFieldError(t, errors.price?.message)}
              {...register('price')}
            />
          </div>

          <div className="sm:max-w-[200px]">
            <Button type="submit" size="lg" disabled={add.isPending}>
              {add.isPending ? t('addForm.submitting') : t('addForm.submit')}
            </Button>
          </div>
        </form>
      </Card>

      <Card
        title={t('positions.title')}
        action={
          <span className="text-mu text-[12.5px]">
            {t('positions.invested')} <span className="text-tx font-mono">{totalInvested(holdings)}</span>
          </span>
        }
      >
        {alerting ? (
          <AlertSettingsForm
            key={alerting.id}
            ticker={alerting.ticker}
            setting={settingFor(alerting.ticker)}
            pending={saveAlert.isPending}
            onSave={saveThreshold}
            onError={setFormError}
            onCancel={closeAlerts}
          />
        ) : null}

        {editing ? (
          <EditHoldingForm
            key={editing.id}
            holding={editing}
            pending={update.isPending}
            onSave={saveCorrection}
            onError={setFormError}
            onCancel={closeEditor}
          />
        ) : null}

        <Table
          caption={t('positions.caption')}
          columns={columns}
          rows={holdings}
          rowKey={(holding) => holding.id}
          empty={t('positions.empty')}
        />
      </Card>

      <ConfirmDialog
        open={removing !== null}
        title={t('removeDialog.title')}
        body={removing ? t('removeDialog.body', { ticker: removing.ticker }) : ''}
        confirmLabel={t('removeDialog.confirm')}
        onCancel={() => setRemoving(null)}
        onConfirm={() => {
          const target = removing
          setRemoving(null)
          if (!target) return

          setFormError('')

          void remove.mutateAsync(target.id).catch(() => {
            setFormError(t('removeDialog.failure', { ticker: target.ticker }))
          })
        }}
      />
    </AppShell>
  )
}
