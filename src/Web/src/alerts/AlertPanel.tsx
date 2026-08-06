import { useState } from 'react'
import { Link } from '@tanstack/react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Button } from '../components/Button'
import { Card } from '../components/Card'
import { formatAge, formatMoney, formatPercent } from '../lib/format'
import { alertHistoryQuery, alertKeys, simulateAlert, type FiredAlert } from './alertsApi'

/**
 * RECENT ACTIVITY, not "active alerts", and the wording is the point. A price alert is a
 * moment that passed — a threshold was crossed at 14:32 — not a condition that is still
 * true now. Titling this "Active alerts" would promise a live list of breached thresholds,
 * which is a different feature nobody built, and every row carries a timestamp so the
 * reading is unambiguous either way.
 */

interface AlertPanelProps {
  /** How many rows to show. The query always holds the server's full page; this only slices. */
  limit?: number
  /** The dashboard's panel offers Simulate and a link onward; the notifications screen is the list. */
  compact?: boolean
}

function DirectionChip({ direction }: { direction: FiredAlert['direction'] }) {
  const falling = direction === 'Fall'

  return (
    <span
      className={`rounded-full px-1.5 py-px text-[10.5px] font-medium tracking-[0.03em] uppercase ${
        falling ? 'bg-dn/12 text-dn' : 'bg-up/12 text-up'
      }`}
    >
      {falling ? 'Fall' : 'Rise'}
    </span>
  )
}

export function AlertRow({ alert }: { alert: FiredAlert }) {
  const falling = alert.direction === 'Fall'
  const firedAt = Date.parse(alert.firedAt)

  return (
    <li className="border-bd/60 flex flex-col gap-1.5 border-b py-3 last:border-0 first:pt-0">
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-tx font-mono text-[13px]">{alert.ticker}</span>
        <DirectionChip direction={alert.direction} />

        {/*
         * Badged, because a simulated alert went through the real path — saved and
         * published like any other — and is therefore indistinguishable from a genuine
         * one unless it says so. That is the whole reason Simulate is worth having.
         */}
        {alert.isSimulated ? (
          <span className="border-bd text-mu rounded-full border px-1.5 py-px text-[10.5px] tracking-[0.03em] uppercase">
            Simulated
          </span>
        ) : null}

        <span className={`ml-auto font-mono text-[13px] ${falling ? 'text-dn' : 'text-up'}`}>
          {formatPercent(alert.changePercent)}
        </span>
      </div>

      <p className="text-mu text-[12px] leading-snug">{alert.reason}</p>

      <div className="text-mu flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1 text-[11.5px]">
        <span className="font-mono">
          {formatMoney(alert.triggerPrice)}
          <span className="text-mu/80"> from {formatMoney(alert.referencePrice)}</span>
        </span>

        {/* Every row is stamped. `title` carries the exact instant for anyone who needs it. */}
        <time dateTime={alert.firedAt} title={alert.firedAt}>
          {Number.isNaN(firedAt) ? alert.firedAt : `${formatAge(Date.now() - firedAt)} ago`}
        </time>
      </div>
    </li>
  )
}

export function AlertPanel({ limit, compact = false }: AlertPanelProps) {
  const queryClient = useQueryClient()
  const [message, setMessage] = useState('')

  const { data, isPending, isError } = useQuery(alertHistoryQuery)

  const simulate = useMutation({
    mutationFn: () => simulateAlert(),
    onSuccess: () => setMessage(''),
    // 409 is the only expected failure: nothing to simulate against yet.
    onError: () => setMessage('Set a threshold on one of your positions first.'),
    // The alert also arrives on the stream. This is what makes Simulate work with the
    // stream down, which is exactly the case the persist-then-publish rule exists for.
    onSettled: () => queryClient.invalidateQueries({ queryKey: alertKeys.history() }),
  })

  const alerts = data ?? []
  const shown = limit === undefined ? alerts : alerts.slice(0, limit)

  return (
    <Card
      title="Recent activity"
      action={
        compact ? (
          <Button
            variant="secondary"
            size="sm"
            onClick={() => simulate.mutate()}
            loading={simulate.isPending}
          >
            Simulate
          </Button>
        ) : null
      }
    >
      {message ? (
        <p role="status" className="text-warn mb-3 text-[12px]">
          {message}
        </p>
      ) : null}

      {isError ? (
        <p role="status" className="text-mu text-[12.5px]">
          Could not load recent alerts. They are saved either way — this list will fill in on
          the next refresh.
        </p>
      ) : null}

      {shown.length === 0 && !isError ? (
        <p className="text-mu text-[12.5px]">
          {isPending
            ? 'Loading recent alerts…'
            : 'Nothing has crossed a threshold yet. Set one on a position, then use Simulate.'}
        </p>
      ) : (
        <ul aria-label="Recent alerts" className="flex flex-col">
          {shown.map((alert) => (
            <AlertRow key={alert.id} alert={alert} />
          ))}
        </ul>
      )}

      {compact && alerts.length > shown.length ? (
        <Link to="/notifications" className="text-ac mt-3 inline-block text-[12px] hover:underline">
          See all {alerts.length}
        </Link>
      ) : null}
    </Card>
  )
}
