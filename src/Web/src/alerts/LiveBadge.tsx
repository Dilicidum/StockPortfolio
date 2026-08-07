import { useTranslation } from 'react-i18next'
import { useAlertStreamStatus, type AlertStreamStatus } from './useAlertStream'

const LABEL_KEYS: Record<AlertStreamStatus, string> = {
  connecting: 'liveBadge.connecting',
  live: 'liveBadge.live',
  reconnecting: 'liveBadge.reconnecting',
  offline: 'liveBadge.offline',
}

const DOTS: Record<AlertStreamStatus, string> = {
  connecting: 'bg-mu',
  live: 'bg-up',
  reconnecting: 'bg-warn',
  offline: 'bg-dn',
}

export function LiveBadge() {
  const { t } = useTranslation('alerts')
  const status = useAlertStreamStatus()

  return (
    <span
      aria-live="polite"
      title={status === 'live' ? t('liveBadge.liveTitle') : t('liveBadge.offlineTitle')}
      className="border-bd bg-panel-2 text-mu flex items-center gap-2 rounded-full border py-[5px] pr-3 pl-[10px] text-[12px]"
    >
      <span aria-hidden="true" className={`h-1.5 w-1.5 rounded-full ${DOTS[status]}`} />
      {t(LABEL_KEYS[status])}
    </span>
  )
}
