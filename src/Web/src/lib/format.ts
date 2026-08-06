import i18n from './i18n'

export interface Money {
  amount: string
  currency: string
}

export const NO_VALUE = '—'

export function formatMoney(money: Money | null | undefined): string {
  if (!money || money.amount === '') return NO_VALUE

  return new Intl.NumberFormat(i18n.language, {
    style: 'currency',
    currency: money.currency,
  }).format(money.amount as `${number}`)
}

export function formatPercent(percent: string | null | undefined): string {
  if (percent === null || percent === undefined || percent === '') return NO_VALUE

  return `${percent}%`
}

export function formatAge(milliseconds: number): string {
  const seconds = Math.max(0, Math.round(milliseconds / 1000))
  if (seconds < 60) return i18n.t('common:time.secondsShort', { count: seconds })

  const minutes = Math.round(seconds / 60)
  if (minutes < 60) return i18n.t('common:time.minutesShort', { count: minutes })

  return i18n.t('common:time.hoursShort', { count: Math.round(minutes / 60) })
}

export function isNegative(money: Money | null | undefined): boolean {
  return money?.amount.startsWith('-') ?? false
}
