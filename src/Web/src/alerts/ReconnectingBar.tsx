import { useTranslation } from 'react-i18next'
import { useAlertStreamStatus, useBrowserOnline } from './useAlertStream'

export function ReconnectingBar() {
  const { t } = useTranslation('alerts')
  const status = useAlertStreamStatus()
  const online = useBrowserOnline()

  const connected = online && status === 'live'
  const settling = online && status === 'connecting'

  if (connected || settling) return null

  return (
    <div
      aria-live="polite"
      className="border-warn/40 bg-warn/10 text-tx flex items-center justify-center gap-2 border-b px-5 py-1.5 text-[12.5px]"
    >
      <span aria-hidden="true" className="bg-warn h-1.5 w-1.5 shrink-0 animate-pulse rounded-full" />
      {online ? t('reconnectBar.reconnecting') : t('reconnectBar.offline')}
    </div>
  )
}
