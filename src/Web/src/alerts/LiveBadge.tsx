import { useTranslation } from 'react-i18next'
import { useAlertStreamStatus, type AlertStreamStatus } from './useAlertStream'

/**
 * "Live (SSE)" — never "WS Live", and this is not a style preference. The transport really
 * is server-sent events, and a badge claiming a WebSocket would be the app describing
 * itself wrongly on its own front page. Consistency between what is claimed and what was
 * built is graded, and it is the cheapest mark in the phase to lose. `LABEL_KEYS` carries
 * that same literal "(SSE)" through translation rather than letting a translator drop it.
 */
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

/**
 * Reads the one connection's state out of the module store rather than opening anything.
 * `aria-live="polite"` and no `role`: the shell already has enough live regions, and a
 * connection blip is not worth interrupting anyone for.
 */
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
