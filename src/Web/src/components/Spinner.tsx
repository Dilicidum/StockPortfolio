import { useTranslation } from 'react-i18next'

export interface SpinnerProps {
  size?: number
  label?: string
}

export function Spinner({ size = 16, label }: SpinnerProps) {
  const { t } = useTranslation('common')

  return (
    <span role="status" className="inline-flex items-center gap-2">
      <span
        aria-hidden="true"
        style={{
          width: size,
          height: size,
          borderWidth: Math.max(2, Math.round(size / 8)),
          animation: 'tz-spin 0.7s linear infinite',
        }}
        className="inline-block rounded-full border-bd border-t-ac"
      />
      {label ? <span className="text-mu text-xs">{label}</span> : <span className="sr-only">{t('loading')}</span>}
    </span>
  )
}
