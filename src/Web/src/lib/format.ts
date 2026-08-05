/**
 * Money crosses the wire as a string and is formatted as one. `Number(money.amount)`
 * would reintroduce exactly the IEEE-754 loss the string serialisation exists to
 * prevent, so the string goes straight to `Intl.NumberFormat.format`, which has
 * accepted string input since ES2023.
 */
export interface Money {
  amount: string
  currency: string
}

/** What an absent value renders as. Never a zero — those are different facts. */
export const NO_VALUE = '—'

export function formatMoney(money: Money | null | undefined): string {
  // Branched BEFORE the formatter on purpose: `format('')` and `format(null)` both
  // render $0.00, so a missing price would be indistinguishable from a worthless one.
  if (!money || money.amount === '') return NO_VALUE

  return new Intl.NumberFormat(undefined, {
    style: 'currency',
    currency: money.currency,
    // The wire type is `string`; the ES2023 overload asks for the literal form of it.
  }).format(money.amount as `${number}`)
}

/** A literal `%`, never `style: 'percent'` — that multiplies by 100 and turns 20.00 into 2000%. */
export function formatPercent(percent: string | null | undefined): string {
  if (percent === null || percent === undefined || percent === '') return NO_VALUE

  return `${percent}%`
}

/** Coarse on purpose: a freshness line that ticks every second is noise, not information. */
export function formatAge(milliseconds: number): string {
  const seconds = Math.max(0, Math.round(milliseconds / 1000))
  if (seconds < 60) return `${seconds}s`

  const minutes = Math.round(seconds / 60)
  if (minutes < 60) return `${minutes}m`

  return `${Math.round(minutes / 60)}h`
}

/** Sign read off the string, because comparing money in the browser is still arithmetic on money. */
export function isNegative(money: Money | null | undefined): boolean {
  return money?.amount.startsWith('-') ?? false
}
