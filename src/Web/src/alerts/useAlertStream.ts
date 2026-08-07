import { useEffect, useSyncExternalStore } from 'react'
import * as signalR from '@microsoft/signalr'
import { useQueryClient, type QueryClient } from '@tanstack/react-query'
import { API_BASE_URL, refreshAccessToken } from '../lib/apiClient'
import { getAccessExpiresAt, getAccessToken } from '../lib/tokenStore'
import {
  ALERT_HISTORY_LIMIT,
  alertKeys,
  ALERT_HUB_PATH,
  ALERT_METHOD_NAME,
  toFiredAlert,
  type AlertNotification,
  type FiredAlert,
} from './alertsApi'

export type AlertStreamStatus = 'connecting' | 'live' | 'reconnecting' | 'offline'

let status: AlertStreamStatus = 'connecting'
const listeners = new Set<() => void>()

function setStatus(next: AlertStreamStatus): void {
  if (status === next) return
  status = next
  for (const listener of listeners) listener()
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}

const getStatus = (): AlertStreamStatus => status

export function useAlertStreamStatus(): AlertStreamStatus {
  return useSyncExternalStore(subscribe, getStatus, getStatus)
}

export function __resetAlertStream(): void {
  setStatus('connecting')
}

const RETRY_DELAYS_MS = [0, 1_000, 2_000, 5_000, 10_000, 30_000]

const RENEW_BEFORE_MS = 30_000

async function accessTokenFactory(): Promise<string> {
  const token = getAccessToken()
  const expiresAt = getAccessExpiresAt()

  const stale =
    token === null ||
    expiresAt === null ||
    new Date(expiresAt).getTime() - Date.now() < RENEW_BEFORE_MS

  if (!stale) return token

  try {
    return await refreshAccessToken()
  } catch {
    return ''
  }
}

function prepend(queryClient: QueryClient, alert: FiredAlert): void {
  queryClient.setQueryData<FiredAlert[]>(alertKeys.history(), (old) => {
    if (old === undefined) return old

    return [alert, ...old.filter((row) => row.id !== alert.id)].slice(0, ALERT_HISTORY_LIMIT)
  })
}

export function useAlertStream(): AlertStreamStatus {
  const queryClient = useQueryClient()

  useEffect(() => {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${API_BASE_URL}${ALERT_HUB_PATH}`, {
        accessTokenFactory,

        transport: signalR.HttpTransportType.WebSockets,
        skipNegotiation: true,
      })
      .withAutomaticReconnect(RETRY_DELAYS_MS)
      .build()

    connection.on(ALERT_METHOD_NAME, (notification: AlertNotification) => {
      prepend(queryClient, toFiredAlert(notification))
    })

    connection.onreconnecting(() => setStatus('reconnecting'))

    connection.onreconnected(() => {
      setStatus('live')

      void queryClient.invalidateQueries({ queryKey: alertKeys.history() })
    })

    connection.onclose(() => setStatus('offline'))

    setStatus('connecting')

    void connection.start().then(
      () => setStatus('live'),
      () => setStatus('offline'),
    )

    return () => {
      void connection.stop()
    }
  }, [queryClient])

  return useAlertStreamStatus()
}
